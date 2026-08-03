using System.Collections.Generic;

namespace Anomaly.Server.Content
{
    /// <summary>
    /// Tracks, per connected client, whether it acknowledged the content catalog with a
    /// matching hash and which entries it failed to register. The server grants a custom id
    /// to a client only when the gate marks that client ready for the entry's key — so a
    /// client that never registered the content can't be sent an item/role it can't render.
    /// </summary>
    internal static class ContentGrantGate
    {
        private sealed class Ack
        {
            public bool HashMatches;
            public readonly HashSet<string> FailedKeys = new HashSet<string>();
        }

        // Keyed by ReferenceHub.netId so we don't hold hub references across the session.
        private static readonly Dictionary<uint, Ack> _byNetId = new Dictionary<uint, Ack>();

        internal static bool TryRecordAck(uint netId, bool hashMatches, IEnumerable<string> failedKeys)
        {
            if (_byNetId.ContainsKey(netId))
                return false;

            var ack = new Ack { HashMatches = hashMatches };
            if (failedKeys != null)
            {
                foreach (var key in failedKeys)
                    ack.FailedKeys.Add(key);
            }
            _byNetId[netId] = ack;
            return true;
        }

        internal static bool IsReady(uint netId) =>
            _byNetId.TryGetValue(netId, out var ack) && ack.HashMatches;

        internal static bool CanGrant(uint netId, string key) =>
            _byNetId.TryGetValue(netId, out var ack) && ack.HashMatches && !ack.FailedKeys.Contains(key);

        internal static void Remove(uint netId) => _byNetId.Remove(netId);

        internal static void Reset() => _byNetId.Clear();
    }
}
