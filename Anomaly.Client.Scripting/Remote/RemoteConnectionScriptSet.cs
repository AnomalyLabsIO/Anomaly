using System.Diagnostics;
using Anomaly.Client.Scripting.Remote.Isolation;
using Anomaly.Client.Scripting.Remote.Protocol;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;

namespace Anomaly.Client.Scripting.Remote;

/// <summary>One transactional remote connection. Guest execution never enters the game's process.</summary>
internal sealed class RemoteConnectionScriptSet : IDisposable {
    private readonly ScriptRuntime _runtime;
    private readonly Func<bool> _current;
    private readonly Func<ScriptToServerMessage, bool>? _send;
    private readonly ulong _activation;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly List<RemoteHostSession> _hosts = new(8);
    private Task<WorkerSandboxProcess>? _launch;
    private RemoteWorkerChannel? _channel;
    private Action<bool, string>? _completed;
    private WireValue? _catalog;
    private RemoteFrame? _active;
    private RemoteFrame? _pendingIncoming;
    private long _command, _request, _launchDeadline;
    private int _loadIndex, _nextCallback, _hostCalls;
    private bool _attempted, _greeted, _configured, _initialized, _failed, _disposed, _resourcesClosed;
    private string _error = "";

    internal RemoteConnectionScriptSet(ScriptRuntime runtime, Func<bool> current,
        Func<ScriptToServerMessage, bool>? send = null, ulong activationId = 0) {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _send = send;
        _activation = activationId;
    }
    internal bool IsInitializing => _attempted && !_initialized && !_failed && !_disposed;
    internal bool IsHealthy {
        get {
            if (!_initialized || _failed || _disposed || !Current() || _channel?.Fault != null) return false;
            foreach (var host in _hosts) if (!host.IsHealthy) return false;
            return true;
        }
    }
    internal string FailureReason => _error;

    internal void BeginInitialize(IReadOnlyList<ScriptSource> sources, bool approved, Action<bool, string> completed) {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(completed);
        if (_attempted || _disposed) throw new InvalidOperationException("A remote connection script set initializes once.");
        _attempted = true;
        _completed = completed;
        try {
            ArgumentNullException.ThrowIfNull(sources);
            if (!Current()) throw new InvalidOperationException("The approved script offer is no longer current.");
            if (sources.Count > ScriptPackLimits.MaxScripts) throw new InvalidDataException("The connection script count exceeds its limit.");
            long bytes = 0;
            var ordered = sources.OrderBy(source => source?.PackId, StringComparer.Ordinal)
                .ThenBy(source => source?.Address, StringComparer.Ordinal).ToArray();
            for (int index = 0; index < ordered.Length; index++) {
                var source = ordered[index] ?? throw new InvalidDataException("A script source is null.");
                source.Validate();
                bytes += source.SourceBytes;
                if (bytes > ScriptPackLimits.MaxScriptBytes) throw new InvalidDataException("The connection script bytes exceed their limit.");
                for (int previous = 0; previous < index; previous++) {
                    if (ordered[previous].Address == source.Address) throw new InvalidDataException("The connection script inventory repeats an address.");
                    if (ordered[previous].PackId == source.PackId && !ordered[previous].HasSameContracts(source))
                        throw new InvalidDataException("Scripts in the same pack must retain the same approved message contracts.");
                }
            }
            if (sources.Count != 0 && !approved) throw new InvalidOperationException("Script execution was not approved for this exact offer.");
            if (sources.Count == 0) { Commit(); return; }
            if (!RemoteWorkerFactory.IsQualified) throw new NotSupportedException(RemoteWorkerFactory.DisabledReason);
            _catalog = RemoteHostSession.ExportCatalog(_runtime);
            foreach (var source in ordered) _hosts.Add(new RemoteHostSession(_runtime, source, _activation, _current, _send));
            _launchDeadline = Stopwatch.GetTimestamp() + 15L * Stopwatch.Frequency;
            _launch = RemoteWorkerFactory.StartWorkerAsync();
        } catch (Exception error) when (error is not StackOverflowException and not OutOfMemoryException) { Fail(error.Message); }
    }

    internal void Tick() {
        EnsureOwner();
        if (_disposed || _failed || !_attempted) return;
        if (!Current()) { Fail("The approved script connection was revoked."); return; }
        foreach (var host in _hosts) if (!host.IsHealthy) { Fail("A remote host capability was retired."); return; }
        if (_hosts.Count == 0) return;
        if (_channel == null) {
            var launch = _launch;
            if (launch == null) { Fail("The remote worker launch was not scheduled."); return; }
            if (!launch.IsCompleted) {
                if (Stopwatch.GetTimestamp() >= _launchDeadline) Fail("The isolated worker launch deadline exceeded.");
                return;
            }
            try { _channel = new RemoteWorkerChannel(launch.GetAwaiter().GetResult()); }
            catch (Exception error) { Fail("Isolated worker launch failed: " + error.Message); return; }
        }
        var channel = _channel;
        if (channel.Fault != null) { Fail(TransportFailure(channel)); return; }
        long started = Stopwatch.GetTimestamp();
        long window = Math.Max(1, Stopwatch.Frequency / 500);
        int operations = 0, callbackStarts = 0;
        try {
            for (int frames = 0; frames < 32; frames++) {
                RemoteFrame frame;
                if (_pendingIncoming != null) { frame = _pendingIncoming; _pendingIncoming = null; }
                else if (!channel.TryRead(out frame!)) break;
                if (channel.Fault != null) {
                    _pendingIncoming = frame;
                    Fail(TransportFailure(channel));
                    return;
                }
                if (frame.Kind == FrameKind.Request && (operations >= 16 || Stopwatch.GetTimestamp() - started >= window)) { _pendingIncoming = frame; break; }
                if (frame.Kind == FrameKind.Request) operations++;
                Handle(frame);
                if (_failed || _disposed || !Current()) return;
                if (_initialized && _active == null && callbackStarts < 4 && TryStartCallback()) callbackStarts++;
            }
            if (_failed || _disposed || channel.Fault != null) return;
            if (_active == null) {
                if (!_configured && _greeted) Start(FrameKind.Configure, 0, _catalog!);
                else if (_configured && _loadIndex < _hosts.Count) Start(FrameKind.Load, _loadIndex + 1, _hosts[_loadIndex].LoadData);
                else if (_configured && !_initialized) Commit();
                else if (_initialized && callbackStarts < 4) TryStartCallback();
            }
        } catch (Exception error) when (error is not StackOverflowException and not OutOfMemoryException) { Fail("Remote script host rejected execution: " + error.Message); }
    }
    private void Handle(RemoteFrame frame) {
        if (frame.Kind == FrameKind.Ready) {
            if (_greeted || _active != null || frame.Data.Required("version").RequireInteger(1, 1) != 1) throw new InvalidDataException("Unexpected worker greeting.");
            _greeted = true;
            _channel!.Disarm();
            return;
        }
        var active = _active;
        if (!_greeted || active == null || frame.Session != active.Session || frame.Command != active.Command)
            throw new InvalidDataException("Stale or unsolicited worker command result.");
        if (frame.Kind == FrameKind.Request) {
            if (active.Kind is not (FrameKind.Load or FrameKind.Invoke or FrameKind.InvokeFunction) || frame.Session < 1 || frame.Session > _hosts.Count ||
                frame.Request != _request + 1 || ++_hostCalls > 256) throw new InvalidDataException("Worker host request identity or quota exceeded.");
            _request = frame.Request;
            if (!Current()) throw new InvalidOperationException("The script connection was revoked before a host operation.");
            var host = _hosts[frame.Session - 1];
            var result = host.Dispatch(frame.Data);
            if (!host.IsHealthy || !Current()) throw new InvalidOperationException("A native host capability was revoked during dispatch.");
            if (!_channel!.TrySend(new RemoteFrame(FrameKind.Response, frame.Session, frame.Command, frame.Request, result)))
                throw new IOException("The worker host response could not be queued.");
            return;
        }
        if (frame.Kind == FrameKind.Fault) throw new InvalidDataException("Worker fault: " + frame.Data.Required("error").RequireString(4096));
        if (frame.Kind != FrameKind.Completed || frame.Request != 0) throw new InvalidDataException("Unexpected worker response kind.");
        string status = frame.Data.Required("status").RequireString(32);
        if (status != "Completed") throw new InvalidOperationException(TerminalFailure(frame));
        _active = null;
        _channel!.Disarm();
        if (active.Kind == FrameKind.Configure) { _configured = true; _catalog = null; }
        else if (active.Kind == FrameKind.Load) _loadIndex++;
    }
    private string TransportFailure(RemoteWorkerChannel channel) {
        // EOF follows a flushed terminal result in the same ordered pipe. Keep
        // that diagnostic, but never dispatch a queued capability after failure.
        try {
            for (int count = 0; count < 9; count++) {
                RemoteFrame frame;
                if (_pendingIncoming != null) { frame = _pendingIncoming; _pendingIncoming = null; }
                else if (!channel.TryRead(out frame!)) break;
                if (_active == null || frame.Command != _active.Command || frame.Session != _active.Session) continue;
                if (frame.Kind == FrameKind.Fault ||
                    (frame.Kind == FrameKind.Completed && frame.Data.Required("status").RequireString(32) != "Completed"))
                    return TerminalFailure(frame);
            }
        } catch (InvalidDataException) { /* The transport fault already requires revocation. */ }
        return channel.Fault + (channel.StandardError.Length == 0 ? "" : " " + channel.StandardError);
    }

    private string TerminalFailure(RemoteFrame frame) {
        if (frame.Kind == FrameKind.Fault) return "Worker fault: " + frame.Data.Required("error").RequireString(4096);
        string address = frame.Session > 0 && frame.Session <= _hosts.Count ? _hosts[frame.Session - 1].LoadData.Required("address").Text : "worker setup";
        return "Remote Lua " + frame.Data.Required("status").RequireString(32) + " in " + address + ": " + frame.Data.Optional("error").RequireOptionalText();
    }

    private void Start(FrameKind kind, int session, WireValue data) {
        if (_active != null || !Current()) throw new InvalidOperationException("The remote command owner is not available.");
        var frame = new RemoteFrame(kind, session, ++_command, 0, data);
        _active = frame;
        _hostCalls = 0;
        _channel!.Arm(TimeSpan.FromSeconds(5));
        if (!_channel.TrySend(frame)) throw new IOException("The remote command could not be queued.");
    }
    private bool TryStartCallback() {
        if (!IsHealthy || _active != null || _hosts.Count == 0) return false;
        for (int visits = 0; visits < _hosts.Count; visits++) {
            int index = _nextCallback++ % _hosts.Count;
            if (!_hosts[index].TryDequeue(out var delivery)) continue;
            Start(FrameKind.Invoke, index + 1, RemoteWorkerFactory.Record(("token", WireValue.String(delivery.Token)), ("args", delivery.Arguments)));
            return true;
        }
        return false;
    }
    internal bool Receive(ScriptToClientMessage message) {
        EnsureOwner();
        if (!IsHealthy) return false;
        bool accepted = false;
        foreach (var host in _hosts) accepted |= host.Receive(message);
        return accepted;
    }
    private bool Current() { try { return _current(); } catch (Exception) { return false; } }
    private void Commit() {
        if (_failed || _disposed || !Current()) { Fail("The script offer expired before initialization committed."); return; }
        _initialized = true;
        var completed = _completed;
        _completed = null;
        completed?.Invoke(true, "");
    }
    private void Fail(string error) {
        if (_failed || _disposed) return;
        _failed = true;
        _error = error.Length <= 768 ? error : error.Substring(0, 768);
        CloseResources();
        var completed = _completed;
        _completed = null;
        completed?.Invoke(false, _error);
    }
    public void Dispose() {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        _completed = null;
        CloseResources();
    }
    private void CloseResources() {
        if (_resourcesClosed) return;
        _resourcesClosed = true;
        _channel?.Dispose();
        _channel = null;
        if (_launch != null) _ = _launch.ContinueWith(task => { if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        foreach (var host in _hosts) host.Dispose();
        _hosts.Clear();
        _catalog = null;
        _active = _pendingIncoming = null;
    }
    private void EnsureOwner() {
        if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Remote scripting must be pumped on its game owner thread.");
    }
}

internal static class RemoteWireError {
    internal static string RequireOptionalText(this WireValue value) => value.Kind == WireKind.Null ? "" : value.RequireString(768);
}
