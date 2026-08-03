using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Config;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Persistence;
using Anomaly.Client.Api.Scheduling;

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
    {
        ModId = modId;
        _beginExecution = beginExecution ?? throw new ArgumentNullException(nameof(beginExecution));
        _endExecution = endExecution ?? throw new ArgumentNullException(nameof(endExecution));
        _faultHandler = faultHandler ?? throw new ArgumentNullException(nameof(faultHandler));
        DataDirectory = Path.Combine(rootDirectory, "Data", modId);
        ConfigDirectory = Path.Combine(rootDirectory, "Config", modId);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Config = ClientConfig.For(modId);
        Persistence = ClientPersistence.For(modId);
    }

    public string ModId { get; }
    public string DataDirectory { get; }
    public string ConfigDirectory { get; }
    public CancellationToken CancellationToken => _cancellation.Token;
    public IConfigSource Config { get; }
    public IPersistenceStore Persistence { get; }

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

    public void Subscribe(Action subscribe, Action unsubscribe)
    {
        ThrowIfDisposed();
        if (subscribe == null) throw new ArgumentNullException(nameof(subscribe));
        if (unsubscribe == null) throw new ArgumentNullException(nameof(unsubscribe));
        subscribe();
        _cleanup.Add(unsubscribe);
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
        _cancellation.Cancel();
        foreach (var task in _scheduledTasks) task.Cancel();
        CommandRegistry.UnregisterAll(ModId);

        for (var i = _cleanup.Count - 1; i >= 0; i--)
        {
            try { _cleanup[i](); }
            catch { /* module cleanup is best-effort and attributed by its owner */ }
        }
        for (var i = _resources.Count - 1; i >= 0; i--)
        {
            try { _resources[i].Dispose(); }
            catch { /* module cleanup is best-effort and attributed by its owner */ }
        }

        FeatureHealth.ClearOwner(ModId);
        _cancellation.Dispose();
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
