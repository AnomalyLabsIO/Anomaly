using System;
using System.Collections.Generic;

namespace Anomaly.Client.Api.Networking;

/// <summary>
/// Coalesces the documented duplicate IL2CPP Harmony invocations for one
/// encrypted-channel receive without creating an unbounded pre-decrypt bypass.
/// State is scoped to one client connection and reset on disconnect.
/// </summary>
internal sealed class InboundEnvelopeDispatchCoalescer
{
    internal const int ExpectedDuplicateInvocations = 2;
    private const int MaxTrackedEnvelopesPerFrame = 64;

    private readonly Dictionary<EnvelopeKey, int> _observed = new();
    private int _frame = int.MinValue;

    internal DispatchDisposition Observe(int frame, byte securityLevel, byte[] envelope)
    {
        if (frame != _frame)
        {
            _observed.Clear();
            _frame = frame;
        }

        var key = EnvelopeKey.Create(securityLevel, envelope);
        if (!_observed.TryGetValue(key, out int duplicateCount))
        {
            if (_observed.Count < MaxTrackedEnvelopesPerFrame)
                _observed.Add(key, 0);
            return DispatchDisposition.Process;
        }

        duplicateCount++;
        _observed[key] = duplicateCount;
        return duplicateCount <= ExpectedDuplicateInvocations
            ? DispatchDisposition.IgnoreExpectedDuplicate
            : DispatchDisposition.ChargeExcessDuplicate;
    }

    internal void Reset()
    {
        _observed.Clear();
        _frame = int.MinValue;
    }

    internal enum DispatchDisposition : byte
    {
        Process = 0,
        IgnoreExpectedDuplicate = 1,
        ChargeExcessDuplicate = 2,
    }

    private readonly struct EnvelopeKey : IEquatable<EnvelopeKey>
    {
        private readonly byte _securityLevel;
        private readonly int _length;
        private readonly ulong _prefixLow;
        private readonly ulong _prefixHigh;

        private EnvelopeKey(byte securityLevel, int length, ulong prefixLow, ulong prefixHigh)
        {
            _securityLevel = securityLevel;
            _length = length;
            _prefixLow = prefixLow;
            _prefixHigh = prefixHigh;
        }

        internal static EnvelopeKey Create(byte securityLevel, byte[] envelope)
        {
            if (envelope == null)
                return new EnvelopeKey(securityLevel, -1, 0, 0);

            ulong low = 0;
            ulong high = 0;
            int prefixLength = Math.Min(envelope.Length, 16);
            for (int index = 0; index < prefixLength; index++)
            {
                if (index < 8)
                    low |= (ulong)envelope[index] << (index * 8);
                else
                    high |= (ulong)envelope[index] << ((index - 8) * 8);
            }
            return new EnvelopeKey(securityLevel, envelope.Length, low, high);
        }

        public bool Equals(EnvelopeKey other) =>
            _securityLevel == other._securityLevel &&
            _length == other._length &&
            _prefixLow == other._prefixLow &&
            _prefixHigh == other._prefixHigh;

        public override bool Equals(object obj) => obj is EnvelopeKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _securityLevel;
                hash = (hash * 397) ^ _length;
                hash = (hash * 397) ^ _prefixLow.GetHashCode();
                hash = (hash * 397) ^ _prefixHigh.GetHashCode();
                return hash;
            }
        }
    }
}
