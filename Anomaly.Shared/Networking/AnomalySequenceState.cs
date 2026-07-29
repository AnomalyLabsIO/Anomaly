using System.Collections.Generic;

namespace Anomaly.Shared.Networking
{
    /// <summary>
    /// Semantic sequence guard for round-scoped messages. This is deliberately
    /// separate from <see cref="AnomalyReplayState"/>: the latter protects the
    /// encrypted transport envelope, while this state prevents a stale,
    /// authenticated gameplay request from a previous portal/pair revision from
    /// being accepted in a later round.
    /// </summary>
    public sealed class AnomalySequenceState
    {
        public enum RxResult : byte
        {
            Accepted,
            RejectedEpoch,
            RejectedSequence
        }

        private readonly object _lock = new object();
        private readonly Dictionary<uint, uint> _lastByHash = new Dictionary<uint, uint>();
        private uint _epoch;

        public RxResult TryAccept(uint expectedEpoch, uint messageEpoch, uint messageSequence, uint messageHash)
        {
            if (messageEpoch == 0 || messageEpoch != expectedEpoch)
                return RxResult.RejectedEpoch;
            if (messageSequence == 0)
                return RxResult.RejectedSequence;

            lock (_lock)
            {
                if (_epoch != expectedEpoch)
                {
                    _epoch = expectedEpoch;
                    _lastByHash.Clear();
                }

                if (_lastByHash.TryGetValue(messageHash, out uint last) && messageSequence <= last)
                    return RxResult.RejectedSequence;

                _lastByHash[messageHash] = messageSequence;
                return RxResult.Accepted;
            }
        }

        public void Reset(uint epoch = 0)
        {
            lock (_lock)
            {
                _epoch = epoch;
                _lastByHash.Clear();
            }
        }
    }
}
