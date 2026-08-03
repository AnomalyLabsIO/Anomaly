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
    public static class AnomalyReplayTable
    {
        private static readonly ConcurrentDictionary<ReferenceHub, AnomalyReplayState> States =
            new ConcurrentDictionary<ReferenceHub, AnomalyReplayState>();
        private static readonly ConcurrentDictionary<ReferenceHub, AnomalySequenceState> SequenceStates =
            new ConcurrentDictionary<ReferenceHub, AnomalySequenceState>();
        private static readonly ConcurrentDictionary<ReferenceHub, AnomalyInboundRateLimiter> RateLimiters =
            new ConcurrentDictionary<ReferenceHub, AnomalyInboundRateLimiter>();

        public static AnomalyReplayState GetOrCreate(ReferenceHub hub)
        {
            return States.GetOrAdd(hub, _ => new AnomalyReplayState());
        }

        public static AnomalySequenceState GetSequenceOrCreate(ReferenceHub hub)
        {
            return SequenceStates.GetOrAdd(hub, _ => new AnomalySequenceState());
        }

        public static AnomalyInboundRateLimiter GetRateLimiterOrCreate(ReferenceHub hub)
        {
            return RateLimiters.GetOrAdd(hub, _ => new AnomalyInboundRateLimiter());
        }

        public static void Remove(ReferenceHub hub)
        {
            if (hub == null) return;
            States.TryRemove(hub, out _);
            SequenceStates.TryRemove(hub, out _);
            RateLimiters.TryRemove(hub, out _);
        }

        public static void Reset()
        {
            States.Clear();
            SequenceStates.Clear();
            RateLimiters.Clear();
        }
    }
}
