using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Config;
using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Persistence;
using Anomaly.Client.Api.Scheduling;
using MelonLoader;

namespace Anomaly.Client.Internal.Modding;

internal sealed class ModContext : IModContext, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<ScheduledTask> _scheduledTasks = new();
    private readonly List<IDisposable> _resources = new();
    private readonly List<Action> _cleanup = new();
    private readonly Action _beginExecution;
    private readonly Action _endExecution;
    private readonly Action<Exception> _faultHandler;
    private bool _disposed;
    private bool _faulted;

    internal ModContext(
        string modId,
        string rootDirectory,
        Action beginExecution,
        Action endExecution,
        Action<Exception> faultHandler)
        : this(modId, rootDirectory, beginExecution, endExecution, faultHandler,
            ClientConfig.For(modId), ClientPersistence.For(modId))
    {
    }

    internal ModContext(
        string modId,
        string rootDirectory,
        Action beginExecution,
        Action endExecution,
        Action<Exception> faultHandler,
        IConfigSource config,
        IPersistenceStore persistence)
    {
        ModId = modId;
        _beginExecution = beginExecution ?? throw new ArgumentNullException(nameof(beginExecution));
        _endExecution = endExecution ?? throw new ArgumentNullException(nameof(endExecution));
        _faultHandler = faultHandler ?? throw new ArgumentNullException(nameof(faultHandler));
        DataDirectory = Path.Combine(rootDirectory, "Data", modId);
        ConfigDirectory = Path.Combine(rootDirectory, "Config", modId);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    public string ModId { get; }
    public string DataDirectory { get; }
    public string ConfigDirectory { get; }
    public CancellationToken CancellationToken => _cancellation.Token;
    public IConfigSource Config { get; }
    public IPersistenceStore Persistence { get; }
    internal bool TeardownFailed { get; private set; }

    public ScheduledTask Schedule(TimeSpan delay, Action callback)
    {
        ThrowIfDisposed();
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        var task = Scheduler.In(delay, () => InvokeOwned(callback), ModId);
        _scheduledTasks.Add(task);
        return task;
    }

    public ScheduledTask ScheduleRecurring(TimeSpan interval, Action callback, TimeSpan firstDelay = default)
    {
        ThrowIfDisposed();
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        var task = Scheduler.Every(interval, () => InvokeOwned(callback), firstDelay, ModId);
        _scheduledTasks.Add(task);
        return task;
    }

    public void RegisterCommand(ICommand command)
    {
        ThrowIfDisposed();
        if (command == null) throw new ArgumentNullException(nameof(command));
        CommandRegistry.Register(ModId, new OwnedCommand(this, command));
    }

    public void Subscribe<TEventArgs>(
        Action<AnomalyEventHandler<TEventArgs>> subscribe,
        Action<AnomalyEventHandler<TEventArgs>> unsubscribe,
        AnomalyEventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs
    {
        ThrowIfDisposed();
        if (subscribe == null) throw new ArgumentNullException(nameof(subscribe));
        if (unsubscribe == null) throw new ArgumentNullException(nameof(unsubscribe));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        AnomalyEventHandler<TEventArgs> ownedHandler =
            args => InvokeOwned(() => handler(args));
        subscribe(ownedHandler);
        _cleanup.Add(() => unsubscribe(ownedHandler));
    }

    public void Track(IDisposable resource)
    {
        ThrowIfDisposed();
        if (resource == null) throw new ArgumentNullException(nameof(resource));
        _resources.Add(resource);
    }

    public void RegisterCleanup(Action cleanup)
    {
        ThrowIfDisposed();
        if (cleanup == null) throw new ArgumentNullException(nameof(cleanup));
        _cleanup.Add(cleanup);
    }

    public void ReportHealth(string featureId, FeatureHealthStatus status, string detail = "") =>
        FeatureHealth.Report(ModId, featureId, status, detail);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // OnDisable/Dispose callbacks may have queued persistence writes. Make
        // them durable before owner-scoped scheduler cleanup cancels debounce
        // handles for this context.
        var steps = new List<CleanupStep>
        {
            new("persistence flush", Persistence.Flush),
            new("cancellation callbacks", () => _cancellation.Cancel()),
            new("scheduled task handles", () =>
            {
                foreach (var task in _scheduledTasks) task.Cancel();
            }),
            new("scheduler ownership", () => Scheduler.ClearOwner(ModId)),
            new("commands", () => CommandRegistry.UnregisterAll(ModId)),
        };

        for (var i = _cleanup.Count - 1; i >= 0; i--)
        {
            steps.Add(new CleanupStep("registered cleanup", _cleanup[i]));
        }
        for (var i = _resources.Count - 1; i >= 0; i--)
        {
            steps.Add(new CleanupStep("tracked resource", _resources[i].Dispose));
        }

        steps.Add(new CleanupStep("feature health", () => FeatureHealth.ClearOwner(ModId)));
        steps.Add(new CleanupStep("cancellation source", _cancellation.Dispose));
        IsolatedCleanup.Run((step, exception) =>
        {
            TeardownFailed = true;
            MelonLogger.Error($"Managed mod '{ModId}' {step} cleanup failed: {exception}");
        }, steps.ToArray());
        _scheduledTasks.Clear();
        _cleanup.Clear();
        _resources.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ModContext));
    }

    private bool InvokeOwned(Action callback)
    {
        if (_disposed || _faulted) return false;
        _beginExecution();
        try
        {
            callback();
            return true;
        }
        catch (Exception ex)
        {
            _faulted = true;
            _faultHandler(ex);
            return false;
        }
        finally
        {
            _endExecution();
        }
    }

    private sealed class OwnedCommand : ICommand
    {
        private readonly ModContext _owner;
        private readonly ICommand _inner;

        internal OwnedCommand(ModContext owner, ICommand inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public string Name => _inner.Name;
        public IReadOnlyList<string> Aliases => _inner.Aliases;
        public string Description => _inner.Description;

        public void Execute(CommandContext context)
        {
            if (!_owner.InvokeOwned(() => _inner.Execute(context)))
                context.Reply("The owning mod faulted and has been disabled for this session.");
        }
    }
}
