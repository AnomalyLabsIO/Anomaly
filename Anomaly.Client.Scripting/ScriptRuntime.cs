using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Catalog;
using Anomaly.Client.Scripting.Events;
using Anomaly.Client.Scripting.Messaging;
using System.Diagnostics;

namespace Anomaly.Client.Scripting;

/// <summary>Owns development and approved script-pack sessions without general filesystem or network access.</summary>
internal sealed class ScriptRuntime : IDisposable
{
    private readonly List<ScriptSession> _sessions;
    private readonly Action<string>? _diagnostic;
    private readonly MoonSharp.Interpreter.MemoryBudget _memory;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _disposed;
    private bool _revoking;
    private int _nextSession;
    private ScriptEventCatalog? _eventCatalog;

    internal ScriptRuntime(Action<string>? diagnostic = null, long allocationLimitBytes = 64L * 1024 * 1024)
    {
        if (allocationLimitBytes < 64 * 1024 || allocationLimitBytes > 256L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(allocationLimitBytes));
        _memory = new MoonSharp.Interpreter.MemoryBudget(allocationLimitBytes, reclaimable: true);
        using var memoryScope = _memory.EnterScope();
        _sessions = MoonSharp.Interpreter.MemoryBudget.Allocate(128, () => new List<ScriptSession>(8));
        // Load the dependency without creating a VM. Bootstrap's critical assembly
        // catalog must see the private engine before any approved execution.
        _ = typeof(MoonSharp.Interpreter.Script).Assembly;
        _diagnostic = diagnostic;
    }

    internal ScriptBindingRegistry Bindings { get; } = new();
    internal long MemoryChargedBytes => _memory.ChargedBytes;
    internal long MemoryTotalAllocatedBytes => _memory.TotalAllocatedBytes;

    internal void DiscoverApi()
    {
        EnsureOwner();
        if (_disposed) throw new ObjectDisposedException(nameof(ScriptRuntime));
        if (_eventCatalog != null) return;
        if (_sessions.Count != 0 || _revoking)
            throw new InvalidOperationException("Discover the API before creating script sessions.");
        // Enum-only metadata from the known game interop assembly and RoundPhase; no owner classes,
        // constructors, static properties, assembly probing or AppDomain scan.
        Bindings.Enums.Discover(typeof(Il2Cpp.ItemType).Assembly, typeof(Anomaly.Shared.Networking.Messages.RoundPhase));
        // Only our known API assembly is scanned for callable object bindings.
        Bindings.AutoDiscoverScriptTypes(typeof(Anomaly.Client.Api.Scripting.ScriptTypeAttribute).Assembly);
        Bindings.RegisterType<ScriptGameContext>();
        _eventCatalog = ScriptEventCatalog.DiscoverApi(Bindings);
    }

    internal ScriptApiCatalog GetCatalog() => Bindings.GetCatalog().WithEvents(
        _eventCatalog?.GetCatalog() ?? Array.Empty<ScriptEventCatalogEntry>()).WithMessages();

    internal ScriptSession CreateDevelopmentSession(ScriptExecutionOptions? options = null) => CreateSession(options);

    internal MoonSharp.Interpreter.MemoryBudget.Scope EnterAllocationScope()
    {
        EnsureOwner();
        if (_disposed) throw new ObjectDisposedException(nameof(ScriptRuntime));
        return _memory.EnterScope();
    }

    internal ScriptSession CreateApprovedSession(ScriptSource source, ScriptExecutionOptions? options = null,
        ScriptMessageContext? messageContext = null)
    {
        // A typed, exact-hash script-pack entry is the only production source path.
        // Consent/connection ownership is checked by ConnectionScriptSet as well.
        RemoteExecutionGate.EnsureAllowed();
        return CreateSourceSession(source, options, messageContext);
    }

    // The caller must first prove ownership of the current non-dedicated hosted
    // connection. Local source admission does not open the remote execution gate.
    internal ScriptSession CreateLocalSession(ScriptSource source, ScriptExecutionOptions? options = null,
        ScriptMessageContext? messageContext = null) => CreateSourceSession(source, options, messageContext);

    private ScriptSession CreateSourceSession(ScriptSource source, ScriptExecutionOptions? options,
        ScriptMessageContext? messageContext)
    {
        EnsureOwner();
        if (_disposed) throw new ObjectDisposedException(nameof(ScriptRuntime));
        ArgumentNullException.ThrowIfNull(source);
        using var memoryScope = _memory.EnterScope();
        source.Validate();
        var session = CreateSession(options ?? new ScriptExecutionOptions
        { InstructionLimit = 200_000, Timeout = TimeSpan.FromMilliseconds(100) });
        try
        {
            if (messageContext != null) session.InstallMessages(messageContext);
            return session;
        }
        catch
        {
            // Session construction is not published until every capability is
            // installed. A failed installation must not occupy an active slot
            // or retain a usable VM until a later connection-wide rollback.
            session.Dispose();
            _sessions.Remove(session);
            throw;
        }
    }

    private ScriptSession CreateSession(ScriptExecutionOptions? options)
    {
        EnsureOwner();
        if (_disposed) throw new ObjectDisposedException(nameof(ScriptRuntime));
        if (_revoking) throw new InvalidOperationException("Cannot create a script session during revocation.");
        _memory.Check();
        _memory.Sweep();
        _sessions.RemoveAll(session => session.IsRetired);
        if (_sessions.Count >= 8) throw new InvalidOperationException("The script session limit has been reached.");
        // Parent preflight covers the session wrapper itself; VM allocations are
        // charged to the session child and the same shared parent pool.
        using var memoryScope = _memory.EnterScope();
        var session = MoonSharp.Interpreter.MemoryBudget.Allocate(512,
            () => new ScriptSession(Bindings, options ?? new ScriptExecutionOptions(), _diagnostic, _eventCatalog, _memory));
        _sessions.Add(session);
        return session;
    }

    internal ScriptSession CreateRemoteSession(string? source, ScriptExecutionOptions? options = null)
    {
        // Raw strings never become approved script-pack entries. Reject before source/options processing or VM allocation.
        throw new NotSupportedException("Raw remote source is disabled; an exact approved script-pack entry is required. Memory containment remains unqualified.");
    }

    internal void RevokeAll()
    {
        EnsureOwner();
        if (_revoking) return;
        _revoking = true;
        try
        {
            // Cleanup must also work when the allocation pool is already terminal.
            while (_sessions.Count != 0)
            {
                var index = _sessions.Count - 1;
                var session = _sessions[index];
                _sessions.RemoveAt(index);
                session.Dispose();
            }
        }
        finally { _revoking = false; }
    }

    internal void QueueEvent(string functionName, params object?[] arguments)
    {
        EnsureOwner();
        if (_disposed) return;
        using var memoryScope = _memory.EnterScope();
        var sessions = MoonSharp.Interpreter.MemoryBudget.Allocate(32L + 8L * _sessions.Count, () => _sessions.ToArray());
        foreach (var session in sessions) session.QueueEvent(functionName, arguments);
    }

    internal void Tick(Func<bool>? mayDispatch = null)
    {
        EnsureOwner();
        if (_disposed) return;
        var started = Stopwatch.GetTimestamp();
        _memory.Sweep();
        var frameBudget = TimeSpan.FromMilliseconds(2);
        var dispatched = 0;
        for (var visited = 0; visited < _sessions.Count; visited++)
        {
            // A required script's failure invalidates the whole connection set;
            // never enter another VM after that failure within this same tick.
            if (mayDispatch != null && !mayDispatch()) break;
            var remaining = frameBudget - TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency);
            if (remaining <= TimeSpan.Zero || dispatched == 4) break;
            _nextSession %= _sessions.Count;
            var session = _sessions[_nextSession++];
            if (session.DispatchQueuedEvent(remaining)) dispatched++;
        }
        _sessions.RemoveAll(session => session.IsRetired);
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        try { RevokeAll(); }
        finally
        {
            _eventCatalog?.Dispose();
            _eventCatalog = null;
        }
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("The scripting runtime must be accessed on its owner thread.");
    }
}
