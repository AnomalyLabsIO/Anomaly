using Anomaly.Shared.Networking;

namespace Anomaly.Client.Api.Networking;

/// <summary>
/// Client-side replay-protection state for the Anomaly 0xFE channel. There's
/// only ever one remote (the server), so state is a single static instance.
/// <see cref="Reset"/> is wired on <c>ClientEvents.Disconnected</c> alongside
/// the other per-session cleanup so a reconnect starts fresh.
/// </summary>
internal static class AnomalyReplayTable
{
    internal static AnomalyReplayState Instance { get; private set; } = new AnomalyReplayState();
    internal static AnomalyInboundRateLimiter InboundRateLimiter { get; private set; } =
        new AnomalyInboundRateLimiter();
    internal static AnomalyInboundRateLimiter RawInboundRateLimiter { get; private set; } =
        AnomalyInboundRateLimiter.CreateClientRaw();
    internal static AnomalyInboundRateLimiter PoseInboundRateLimiter { get; private set; } =
        AnomalyInboundRateLimiter.CreatePoseTraffic();
    internal static BulkReplayState Bulk { get; private set; } = new BulkReplayState();
    internal static AnomalyInboundRateLimiter BulkInboundRateLimiter { get; private set; } =
        AnomalyInboundRateLimiter.CreateBulkTraffic();
    private static AnomalyInboundRateLimiter _bulkRaw = AnomalyInboundRateLimiter.CreateClientBulkRaw();

    internal static AnomalyInboundRateLimiter CurrentRawInboundRateLimiter =>
        AnomalyMessaging.BulkReceiveExpected?.Invoke() == true ? _bulkRaw : RawInboundRateLimiter;

    internal static void ResetBulk()
    {
        Bulk = new BulkReplayState();
        BulkInboundRateLimiter = AnomalyInboundRateLimiter.CreateBulkTraffic();
        _bulkRaw = AnomalyInboundRateLimiter.CreateClientBulkRaw();
    }

    internal static void Reset()
    {
        Instance = new AnomalyReplayState();
        InboundRateLimiter = new AnomalyInboundRateLimiter();
        RawInboundRateLimiter = AnomalyInboundRateLimiter.CreateClientRaw();
        PoseInboundRateLimiter = AnomalyInboundRateLimiter.CreatePoseTraffic();
        ResetBulk();
    }
}
