using System.Diagnostics;
using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting.Output;

/// <summary>Runtime-owned, main-thread-only output quotas and expiring owner slots.</summary>
internal sealed class ScriptOutputHost : IDisposable
{
    internal const int MaxDisplayOwners = 8;
    internal const int SessionLogsPerSecond = 4;
    internal const int RuntimeLogsPerSecond = 16;
    internal const int SessionDisplaysPerSecond = 2;
    internal const int RuntimeDisplaysPerSecond = 8;
    // Includes bounded TMP mesh/material workspace retained by a visible label.
    internal const int DisplayStorageBytes = 256 * 1024;
    private readonly Action<string>? _log;
    private readonly IScriptTextSink? _sink;
    private readonly Func<double> _clock;
    private readonly ScriptOutput?[] _slots;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private RateWindow _logs, _displays;
    private double _lastTime;
    private bool _disposed;

    internal ScriptOutputHost(Action<string>? log = null, IScriptTextSink? textSink = null,
        Func<double>? monotonicSeconds = null)
    {
        _log = log;
        _sink = textSink;
        _clock = monotonicSeconds ?? Seconds;
        _slots = MemoryBudget.Allocate(32 + MaxDisplayOwners * 8, () => new ScriptOutput?[MaxDisplayOwners]);
    }

    internal ScriptOutput CreateOutput(Script owner, string sourceIdentity)
    {
        EnsureOwner();
        ObjectDisposedExceptionIfNeeded();
        ArgumentNullException.ThrowIfNull(owner);
        using var scope = owner.MemoryBudget?.EnterScope();
        using var reservation = MemoryBudget.Reserve(256);
        var output = new ScriptOutput(this, owner, sourceIdentity);
        reservation.Bind(output);
        return output;
    }

    internal bool IsAvailable => !_disposed && Environment.CurrentManagedThreadId == _ownerThread;
    internal bool HasLog => _log != null;
    internal bool HasDisplay => _sink != null;

    internal double Now()
    {
        var value = _clock();
        // Production uses Stopwatch. Clamp an injected/regressing clock so expiry
        // and rate windows can never run backwards or be reset by a bad timestamp.
        if (double.IsFinite(value) && value > _lastTime) _lastTime = value;
        return _lastTime;
    }

    internal bool TryRate(bool display, ref RateWindow session, double now)
    {
        ref var runtime = ref (display ? ref _displays : ref _logs);
        var sessionLimit = display ? SessionDisplaysPerSecond : SessionLogsPerSecond;
        var runtimeLimit = display ? RuntimeDisplaysPerSecond : RuntimeLogsPerSecond;
        if (!session.HasRoom(now, sessionLimit) || !runtime.HasRoom(now, runtimeLimit)) return false;
        session.Count++;
        runtime.Count++;
        return true;
    }

    internal bool WriteLog(string text)
    {
        try { _log!(text); return true; }
        catch (Exception exception) when (IsRecoverable(exception)) { return false; }
    }

    internal bool Show(ScriptOutput output, string text)
    {
        var slot = output.Slot;
        if (slot < 0)
        {
            for (var i = 0; i < _slots.Length; i++)
                if (_slots[i] == null) { slot = i; break; }
        }
        if (slot < 0) return false;
        var completed = false;
        try
        {
            if (!_sink!.Set(slot, text)) return false;
            _slots[slot] = output;
            output.Slot = slot;
            completed = true;
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return false;
        }
        finally
        {
            if (!completed)
            {
                if (output.Slot == slot) Clear(output);
                else ClearSink(slot);
            }
        }
    }

    internal bool HasSlot(ScriptOutput output)
    {
        if (output.Slot >= 0) return true;
        for (var i = 0; i < _slots.Length; i++) if (_slots[i] == null) return true;
        return false;
    }

    internal void Clear(ScriptOutput output)
    {
        var slot = output.Slot;
        output.Slot = -1;
        output.ForgetDisplay();
        if (slot < 0 || !ReferenceEquals(_slots[slot], output)) return;
        _slots[slot] = null;
        ClearSink(slot);
    }

    private void ClearSink(int slot)
    {
        try { _sink?.Clear(slot); }
        catch (Exception exception) when (IsRecoverable(exception)) { }
    }

    internal void Tick()
    {
        EnsureOwner();
        if (_disposed) return;
        var now = Now();
        for (var i = 0; i < _slots.Length; i++)
            if (_slots[i] is { } output && now >= output.ExpiresAt) Clear(output);
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        for (var i = 0; i < _slots.Length; i++)
            if (_slots[i] is { } output) Clear(output);
        try { _sink?.Dispose(); }
        catch (Exception exception) when (IsRecoverable(exception)) { }
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Script output must run on its runtime's owning thread.");
    }

    private void ObjectDisposedExceptionIfNeeded()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ScriptOutputHost));
    }

    private static double Seconds() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    internal static bool IsRecoverable(Exception exception) => exception is not OutOfMemoryException and
        not StackOverflowException and not ScriptExecutionAbortedException;

    internal struct RateWindow
    {
        private double _start;
        internal int Count;
        internal bool HasRoom(double now, int limit)
        {
            if (now - _start >= 1) { _start = now; Count = 0; }
            return Count < limit;
        }
    }
}
