using System;
using System.Threading;

namespace Anomaly.Shared.Networking
{
    /// <summary>
    /// Per-(connection × Mirror transport channel) replay-protection state for the
    /// Anomaly 0xFE channel. Ordered channels use a strict monotonic counter
    /// (matches vanilla's <c>EncryptedChannelManager._rxCounter</c>). Unordered
    /// channels use a 128-bit IPsec-style sliding window so legitimate reordering
    /// within the transport is accepted while duplicates are rejected.
    /// </summary>
    public sealed class AnomalyReplayState
    {
        public const int WindowBits = 128;
        private const int LogSuppressMs = 1000;

        public enum RxResult : byte
        {
            Accepted,
            RejectedReplay,
            RejectedStale,
            RejectedInvalid
        }

        private sealed class ChannelEntry
        {
            public readonly object Lock = new object();
            // int64 so Interlocked.Increment wraps cleanly; we cast to uint on the wire,
            // giving the same 32-bit wrap semantics as vanilla's _txCounter.
            public long TxCounter;
            public uint RxHighWater;
            public ulong RxBitmap0; // covers [highWater - 63, highWater]
            public ulong RxBitmap1; // covers [highWater - 127, highWater - 64]
            public int LastLogTickMs;
        }

        // Sized to the MessageChannel enum. Index by (int)channel.
        private readonly ChannelEntry[] _entries = new ChannelEntry[5];

        public AnomalyReplayState()
        {
            for (int i = 0; i < _entries.Length; i++)
                _entries[i] = new ChannelEntry();
        }

        public uint NextTxCounter(MessageChannel channel)
        {
            int idx = (int)channel;
            if ((uint)idx >= (uint)_entries.Length) return 0;
            return unchecked((uint)Interlocked.Increment(ref _entries[idx].TxCounter));
        }

        public RxResult TryAcceptRx(MessageChannel channel, uint counter, out bool shouldLog)
        {
            shouldLog = false;
            if (counter == 0) return RxResult.RejectedInvalid;

            int idx = (int)channel;
            if ((uint)idx >= (uint)_entries.Length) return RxResult.RejectedInvalid;

            var entry = _entries[idx];
            lock (entry.Lock)
            {
                RxResult result = IsOrderedChannel(channel)
                    ? TryAcceptOrdered(entry, counter)
                    : TryAcceptUnordered(entry, counter);

                if (result != RxResult.Accepted)
                {
                    int now = Environment.TickCount;
                    if (unchecked(now - entry.LastLogTickMs) >= LogSuppressMs)
                    {
                        entry.LastLogTickMs = now;
                        shouldLog = true;
                    }
                }
                return result;
            }
        }

        private static RxResult TryAcceptOrdered(ChannelEntry entry, uint counter)
        {
            if (counter <= entry.RxHighWater)
                return counter == entry.RxHighWater ? RxResult.RejectedReplay : RxResult.RejectedStale;
            entry.RxHighWater = counter;
            return RxResult.Accepted;
        }

        private static RxResult TryAcceptUnordered(ChannelEntry entry, uint counter)
        {
            if (counter > entry.RxHighWater)
            {
                uint shift = counter - entry.RxHighWater;
                if (shift >= WindowBits)
                {
                    entry.RxBitmap1 = 0UL;
                    entry.RxBitmap0 = 1UL;
                }
                else if (shift >= 64)
                {
                    entry.RxBitmap1 = entry.RxBitmap0 << (int)(shift - 64);
                    entry.RxBitmap0 = 1UL;
                }
                else
                {
                    entry.RxBitmap1 = (entry.RxBitmap1 << (int)shift) | (entry.RxBitmap0 >> (int)(64 - shift));
                    entry.RxBitmap0 = (entry.RxBitmap0 << (int)shift) | 1UL;
                }
                entry.RxHighWater = counter;
                return RxResult.Accepted;
            }

            uint age = entry.RxHighWater - counter;
            if (age >= WindowBits) return RxResult.RejectedStale;

            if (age < 64)
            {
                ulong mask = 1UL << (int)age;
                if ((entry.RxBitmap0 & mask) != 0) return RxResult.RejectedReplay;
                entry.RxBitmap0 |= mask;
            }
            else
            {
                ulong mask = 1UL << (int)(age - 64);
                if ((entry.RxBitmap1 & mask) != 0) return RxResult.RejectedReplay;
                entry.RxBitmap1 |= mask;
            }
            return RxResult.Accepted;
        }

        private static bool IsOrderedChannel(MessageChannel channel)
        {
            switch (channel)
            {
                case MessageChannel.ReliableOrdered:
                case MessageChannel.Sequenced:
                case MessageChannel.ReliableSequenced:
                    return true;
                case MessageChannel.Unreliable:
                case MessageChannel.ReliableUnordered:
                    return false;
                default:
                    return true;
            }
        }
    }
}
