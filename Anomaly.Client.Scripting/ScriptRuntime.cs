using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Catalog;
using Anomaly.Client.Scripting.Events;
using System.Diagnostics;

namespace Anomaly.Client.Scripting;

/// <summary>Owns development sessions. No filesystem, network or remote execution loader exists.</summary>
internal sealed class ScriptRuntime : IDisposable
{
    private readonly List<ScriptSession> _sessions = new();
    private readonly Action<string>? _diagnostic;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _disposed;
    private bool _revoking;
    private int _nextSession;
    private ScriptEventCatalog? _eventCatalog;

    internal ScriptRuntime(Action<string>? diagnostic = null)
    {
        // Load the dependency without creating a VM. Bootstrap's critical assembly
        // catalog must see the private engine even while execution stays disabled.
        _ = typeof(MoonSharp.Interpreter.Script).Assembly;
        _diagnostic = diagnostic;
    }

    internal ScriptBindingRegistry Bindings { get; } = new();

    internal void DiscoverApi()
    {
        EnsureOwner();
        if (_disposed) throw new ObjectDisposedException(nameof(ScriptRuntime));
        if (_eventCatalog != null) return;
        if (_sessions.Count != 0 || _revoking)
            throw new InvalidOperationException("Discover the API before creating script sessions.");
        // Only our known API assembly is scanned. No assembly probing or AppDomain scan.
        Bindings.AutoDiscoverScriptTypes(typeof(Anomaly.Client.Api.Scripting.ScriptTypeAttribute).Assembly);
        _eventCatalog = ScriptEventCatalog.DiscoverApi(Bindings);
    }

    internal ScriptApiCatalog GetCatalog() => Bindings.GetCatalog().WithEvents(
        _eventCatalog?.GetCatalog() ?? Array.Empty<ScriptEventCatalogEntry>());

    internal ScriptSession CreateDevelopmentSession(ScriptExecutionOptions? options = null)
    {
        EnsureOwner();
        if (_disposed) throw new ObjectDisposedException(nameof(ScriptRuntime));
        if (_revoking) throw new InvalidOperationException("Cannot create a script session during revocation.");
        _sessions.RemoveAll(session => session.IsRetired);
        if (_sessions.Count >= 8) throw new InvalidOperationException("The development session limit has been reached.");
        var session = new ScriptSession(Bindings, options ?? new ScriptExecutionOptions(), _diagnostic, _eventCatalog);
        _sessions.Add(session);
        return session;
    }

    internal ScriptSession CreateRemoteSession(string? source, ScriptExecutionOptions? options = null)
    {
        // This must precede source parsing, VM allocation and even source validation.
        RemoteExecutionGate.EnsureAllowed();
        throw new NotSupportedException("No remote script execution path is installed.");
    }

    internal void RevokeAll()
    {
        EnsureOwner();
        if (_revoking) return;
        _revoking = true;
        var sessions = _sessions.ToArray();
        _sessions.Clear();
        try
        {
            foreach (var session in sessions) session.Dispose();
        }
        finally { _revoking = false; }
    }

    internal void QueueEvent(string functionName, params object?[] arguments)
    {
        EnsureOwner();
        if (_disposed) return;
        foreach (var session in _sessions.ToArray()) session.QueueEvent(functionName, arguments);
    }

    internal void Tick()
    {
        EnsureOwner();
        if (_disposed) return;
        var started = Stopwatch.GetTimestamp();
        var frameBudget = TimeSpan.FromMilliseconds(2);
        var dispatched = 0;
        for (var visited = 0; visited < _sessions.Count; visited++)
        {
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
