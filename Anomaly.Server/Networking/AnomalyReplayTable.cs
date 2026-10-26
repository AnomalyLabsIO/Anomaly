using System.Collections.Concurrent;
using Anomaly.Shared.Networking;

namespace Anomaly.Server.Networking
{
    /// <summary>
    /// Per-hub replay-protection state for the Anomaly 0xFE channel. One
    /// <see cref="AnomalyReplayState"/> instance per connected
    /// <see cref="ReferenceHub"/>. Entries are created lazily by
    /// <see cref="GetOrCreate"/> and dropped on player disconnect via
    /// <see cref="Remove"/> (wired in Plugin.Enable's PlayerEvents.Left handler).
    /// </summary>
    internal static class AnomalyReplayTable
    {
        private static readonly ConcurrentDictionary<ReferenceHub, AnomalyReplayState> States =
            new ConcurrentDictionary<ReferenceHub, AnomalyReplayState>();
        private static readonly ConcurrentDictionary<ReferenceHub, AnomalySequenceState> SequenceStates =
            new ConcurrentDictionary<ReferenceHub, AnomalySequenceState>();
        private static readonly ConcurrentDictionary<ReferenceHub, AnomalyInboundRateLimiter> RateLimiters =
            new ConcurrentDictionary<ReferenceHub, AnomalyInboundRateLimiter>();
        private static readonly ConcurrentDictionary<ReferenceHub, AnomalyInboundRateLimiter> RawRateLimiters =
            new ConcurrentDictionary<ReferenceHub, AnomalyInboundRateLimiter>();

        internal static AnomalyReplayState GetOrCreate(ReferenceHub hub)
        {
            return AuthenticatedTransportSessions.GetReplayState(hub) ??
                   States.GetOrAdd(hub, _ => new AnomalyReplayState());
        }

        internal static AnomalySequenceState GetSequenceOrCreate(ReferenceHub hub)
        {
            return SequenceStates.GetOrAdd(hub, _ => new AnomalySequenceState());
        }

        internal static AnomalyInboundRateLimiter GetRateLimiterOrCreate(ReferenceHub hub)
        {
            return AuthenticatedTransportSessions.GetRateLimiter(hub, raw: false) ??
                   RateLimiters.GetOrAdd(hub, _ => new AnomalyInboundRateLimiter());
        }

        internal static AnomalyInboundRateLimiter GetRawRateLimiterOrCreate(ReferenceHub hub)
        {
            return AuthenticatedTransportSessions.GetRateLimiter(hub, raw: true) ??
                   RawRateLimiters.GetOrAdd(hub, _ => new AnomalyInboundRateLimiter());
        }

        internal static void Remove(ReferenceHub hub)
        {
            if (hub == null) return;
            States.TryRemove(hub, out _);
            SequenceStates.TryRemove(hub, out _);
            RateLimiters.TryRemove(hub, out _);
            RawRateLimiters.TryRemove(hub, out _);
        }

        internal static void Reset()
        {
            States.Clear();
            SequenceStates.Clear();
            RateLimiters.Clear();
            RawRateLimiters.Clear();
        }
    }
}
