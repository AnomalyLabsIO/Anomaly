using System;
using System.Threading;

namespace Anomaly.Client.Auth;

/// <summary>
/// Each connection owns its send failure. A worker retaining an older scope can
/// stop its own send without poisoning a new connection or in-process host.
/// </summary>
internal sealed class AdmissionSendScope<T> where T : class
{
    private T _admission;
    private string _failure;
    private int _failureHandled;
    private int _bound;
    private long _preparedWriter;

    internal T Admission => Volatile.Read(ref _admission);
    internal string Failure => Volatile.Read(ref _failure);
    internal bool IsPreparedWriter(long pointer, bool localHostActive = false)
    {
        long prepared = Volatile.Read(ref _preparedWriter);
        return pointer != 0 && (prepared == pointer || (prepared == -1 && localHostActive));
    }

    // GetConnectData and ConnectClient must use the same native writer. A local
    // in-process host has an explicit allowance only while that host is active.
    internal void PrepareWriter(long pointer)
    {
        if (pointer == 0 || pointer == -1) throw new ArgumentOutOfRangeException(nameof(pointer));
        Volatile.Write(ref _preparedWriter, pointer);
    }
    internal void PermitLocalHost() => Volatile.Write(ref _preparedWriter, -1);
    internal void RevokePreparedWriter() => Volatile.Write(ref _preparedWriter, 0);

    internal void Bind(T admission)
    {
        if (admission == null)
            throw new ArgumentNullException(nameof(admission));
        if (Interlocked.CompareExchange(ref _bound, 1, 0) != 0)
            throw new InvalidOperationException("This send scope already has a frozen admission claim.");
        Volatile.Write(ref _admission, admission);
    }

    // Teardown releases session data but cannot re-enable sends from a failed scope.
    internal void ClearAdmission() => Volatile.Write(ref _admission, null);

    internal void Fail(string reason) => Interlocked.CompareExchange(ref _failure,
        string.IsNullOrEmpty(reason) ? "Authentication integrity verification failed." : reason, null);

    internal bool TryBeginFailureHandling(out string reason)
    {
        reason = Failure;
        return reason != null && Interlocked.CompareExchange(ref _failureHandled, 1, 0) == 0;
    }
}

internal sealed class AdmissionSendGate<T> where T : class
{
    private AdmissionSendScope<T> _current = new AdmissionSendScope<T>();

    internal AdmissionSendScope<T> Current => Volatile.Read(ref _current);
    internal bool IsCurrent(AdmissionSendScope<T> scope) => ReferenceEquals(Current, scope);

    // Called on the main thread only for initialization, an explicit new user
    // connection, or the verified in-process local-host transition.
    internal void BeginScope() => Interlocked.Exchange(ref _current, new AdmissionSendScope<T>());

    internal bool TryBeginFailureHandling(AdmissionSendScope<T> scope, out string reason)
    {
        reason = null;
        return IsCurrent(scope) && scope.TryBeginFailureHandling(out reason);
    }
}
