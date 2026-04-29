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

        public static AnomalyReplayState GetOrCreate(ReferenceHub hub)
        {
            return States.GetOrAdd(hub, _ => new AnomalyReplayState());
        }

        public static void Remove(ReferenceHub hub)
        {
            if (hub == null) return;
            States.TryRemove(hub, out _);
        }
    }
}
