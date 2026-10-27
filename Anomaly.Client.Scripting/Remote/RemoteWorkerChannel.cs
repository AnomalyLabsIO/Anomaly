using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Anomaly.Client.Scripting.Remote.Isolation;
using Anomaly.Client.Scripting.Remote.Protocol;

namespace Anomaly.Client.Scripting.Remote;

/// <summary>Bounded transport only. Never invokes game objects or user callbacks off-thread.</summary>
internal sealed class RemoteWorkerChannel : IDisposable {
    private readonly WorkerSandboxProcess _process;
    private readonly Channel<RemoteFrame> _incoming = Channel.CreateBounded<RemoteFrame>(new BoundedChannelOptions(8) { SingleReader = true, SingleWriter = true });
    private readonly Channel<RemoteFrame> _outgoing = Channel.CreateBounded<RemoteFrame>(new BoundedChannelOptions(8) { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _watchdog;
    private long _deadline = long.MaxValue;
    private string? _fault;
    private string _stderr = "";
    private int _disposed;

    internal RemoteWorkerChannel(WorkerSandboxProcess process) {
        _process = process;
        Arm(TimeSpan.FromSeconds(15));
        // Blocking pipe readers and the hard watchdog must not depend on the
        // game's shared thread pool having spare workers.
        _watchdog = new Thread(WatchdogLoop) { IsBackground = true, Name = "Anomaly remote Lua watchdog" };
        try {
            _watchdog.Start();
            _ = Task.Factory.StartNew(ReadLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _ = Task.Run(WriteLoop);
            _ = Task.Factory.StartNew(ReadErrors, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        } catch { Dispose(); throw; }
    }
    internal string? Fault => Volatile.Read(ref _fault);
    internal string StandardError => Volatile.Read(ref _stderr);
    internal WorkerSandboxProcess Process => _process;
    internal bool TryRead(out RemoteFrame frame) => _incoming.Reader.TryRead(out frame!);
    internal async Task<RemoteFrame> ReadAsync(CancellationToken cancellation = default) => await _incoming.Reader.ReadAsync(cancellation).ConfigureAwait(false);
    internal bool TrySend(RemoteFrame frame) {
        if (Fault != null || Volatile.Read(ref _disposed) != 0) return false;
        if (_outgoing.Writer.TryWrite(frame)) return true;
        Fail("Worker output queue limit exceeded.");
        return false;
    }
    internal void Arm(TimeSpan timeout) {
        long now = Stopwatch.GetTimestamp();
        Interlocked.Exchange(ref _deadline, now + (long)(timeout.TotalSeconds * Stopwatch.Frequency));
    }
    internal void Disarm() => Interlocked.Exchange(ref _deadline, long.MaxValue);
    private void WatchdogLoop() {
        while (Volatile.Read(ref _disposed) == 0) {
            Thread.Sleep(25);
            if (Volatile.Read(ref _disposed) != 0 || Fault != null) continue;
            if (Stopwatch.GetTimestamp() >= Interlocked.Read(ref _deadline)) { Fail("Worker independent wall deadline exceeded."); continue; }
            try { if (_process.HasExited) Fail("Isolated worker exited."); }
            catch (Exception) { Fail("Isolated worker process became unavailable."); }
        }
    }
    private void ReadLoop() {
        try {
            while (Volatile.Read(ref _disposed) == 0 && Fault == null) {
                var frame = RemoteProtocol.Read(_process.Output, fromWorker: true);
                if (frame == null) { Fail("Worker protocol ended."); return; }
                if (!_incoming.Writer.TryWrite(frame)) { Fail("Worker input queue limit exceeded."); return; }
            }
        } catch (Exception) { if (Volatile.Read(ref _disposed) == 0) Fail("Invalid or interrupted worker protocol."); }
        finally { _incoming.Writer.TryComplete(new IOException(Fault ?? "Worker disposed.")); }
    }
    private async Task WriteLoop() {
        try {
            while (await _outgoing.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
                while (_outgoing.Reader.TryRead(out var frame)) RemoteProtocol.Write(_process.Input, frame);
        } catch (Exception) { if (Volatile.Read(ref _disposed) == 0) Fail("Worker command pipe failed."); }
    }
    private void ReadErrors() {
        // A compromised worker cannot use diagnostic output as an unbounded host allocator or CPU sink.
        var buffer = new byte[512];
        var text = new StringBuilder(1024);
        int total = 0;
        try {
            while (Volatile.Read(ref _disposed) == 0) {
                int count = _process.Error.Read(buffer, 0, buffer.Length);
                if (count == 0) return;
                total += count;
                if (total > 8192) { Fail("Worker diagnostic byte limit exceeded."); return; }
                if (text.Length < 2048) {
                    text.Append(Encoding.UTF8.GetString(buffer, 0, Math.Min(count, 2048 - text.Length)));
                    Volatile.Write(ref _stderr, text.ToString());
                }
            }
        } catch (Exception) { /* Job teardown closes all inherited pipes. */ }
    }
    internal void Fail(string reason) {
        if (Interlocked.CompareExchange(ref _fault, reason, null) != null) return;
        try { _process.Kill(); }
        catch (Exception) { _process.Dispose(); } // Closing the verified kill-on-close job is the fail-safe.
        finally {
            _incoming.Writer.TryComplete(new IOException(reason));
            _outgoing.Writer.TryComplete();
        }
    }
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _outgoing.Writer.TryComplete();
        _incoming.Writer.TryComplete();
        _process.Dispose();
        // Do not wait for reader/writer or profile cleanup on the game's owner thread.
    }
}
