using System;
using System.Diagnostics;
using Anomaly.Shared.Content;

namespace Anomaly.Server.Networking;

/// <summary>One exact offer and an absolute, monotonic admission deadline.</summary>
internal sealed class ConnectionContentState
{
    internal const int TimeoutSeconds = 120;
    private enum Phase { Pending, Offered, Approved, Transferring, Transferred, Complete, Rejected }
    private readonly Func<double> _elapsed;
    private Phase _phase;
    private byte[] _catalogHash;
    private byte[] _manifestHash;

    internal ConnectionContentState(Func<double> elapsedSeconds = null)
    {
        var timer = Stopwatch.StartNew();
        _elapsed = elapsedSeconds ?? (() => timer.Elapsed.TotalSeconds);
    }

    internal ulong SessionId { get; private set; }
    internal double RemainingSeconds => Math.Max(0, TimeoutSeconds - _elapsed());
    internal bool DeadlineElapsed => _elapsed() >= TimeoutSeconds;
    internal bool Expired => _phase != Phase.Complete && DeadlineElapsed;
    internal ConnectionContentState PreserveDeadline() => new ConnectionContentState(_elapsed);
    internal bool Complete => _phase == Phase.Complete;
    internal bool Rejected => _phase == Phase.Rejected;
    internal bool HasOffer => _phase != Phase.Pending && !Rejected;
    internal byte[] CatalogHash => (byte[])_catalogHash.Clone();
    internal byte[] ManifestHash => (byte[])_manifestHash.Clone();

    internal bool Offer(ulong sessionId, byte[] catalogHash, byte[] manifestHash)
    {
        if (!CanContinue || _phase != Phase.Pending || sessionId == 0 ||
            catalogHash?.Length != 32 || manifestHash?.Length != 32) return false;
        SessionId = sessionId;
        _catalogHash = (byte[])catalogHash.Clone();
        _manifestHash = (byte[])manifestHash.Clone();
        _phase = Phase.Offered;
        return true;
    }

    internal bool Approve(byte[] catalogHash, byte[] manifestHash) =>
        Advance(Phase.Offered, Phase.Approved, catalogHash, manifestHash);

    internal bool BeginTransfer(ulong sessionId)
    {
        if (!CanContinue || _phase != Phase.Approved || SessionId != sessionId) return false;
        _phase = Phase.Transferring;
        return true;
    }

    internal bool FinishTransfer()
    {
        if (!CanContinue || _phase != Phase.Transferring) return false;
        _phase = Phase.Transferred;
        return true;
    }

    internal bool Activate(byte[] catalogHash, byte[] manifestHash) =>
        Advance(Phase.Transferred, Phase.Complete, catalogHash, manifestHash);

    internal bool Reject()
    {
        if (Rejected) return false;
        _phase = Phase.Rejected;
        return true;
    }

    private bool CanContinue => !Rejected && !Complete && !Expired;
    private bool Advance(Phase expected, Phase next, byte[] catalogHash, byte[] manifestHash)
    {
        if (!CanContinue || _phase != expected ||
            !CatalogBuilder.HashesEqual(_catalogHash, catalogHash) ||
            !CatalogBuilder.HashesEqual(_manifestHash, manifestHash)) return false;
        _phase = next;
        return true;
    }
}
