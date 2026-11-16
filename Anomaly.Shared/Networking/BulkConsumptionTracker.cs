using System;
using System.Collections.Generic;

namespace Anomaly.Shared.Networking;

/// <summary>Advances only through contiguous disk-consumed counters, retaining a bounded hole set.</summary>
internal sealed class BulkConsumptionTracker
{
    private readonly uint[] _consumed;
    private readonly HashSet<uint>[] _later = new HashSet<uint>[BulkTransferProtocol.MaxDomains];
    private readonly int _domainCount;

    internal BulkConsumptionTracker(uint[] counterBases, int domainCount)
    {
        if (domainCount < 1 || domainCount > BulkTransferProtocol.MaxDomains)
            throw new ArgumentOutOfRangeException(nameof(domainCount));
        if (counterBases != null && counterBases.Length != BulkTransferProtocol.MaxDomains)
            throw new ArgumentException("Invalid bulk counter bases.", nameof(counterBases));
        _consumed = counterBases == null ? new uint[BulkTransferProtocol.MaxDomains] : (uint[])counterBases.Clone();
        foreach (uint counter in _consumed)
            if (counter == uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(counterBases));
        _domainCount = domainCount;
        for (int domain = 0; domain < _later.Length; domain++) _later[domain] = new HashSet<uint>();
    }

    internal bool TryConsume(int domain, uint counter)
    {
        if ((uint)domain >= (uint)_domainCount || counter == uint.MaxValue || counter <= _consumed[domain] ||
            counter - _consumed[domain] > BulkTransferProtocol.MaxOutstandingFramesPerDomain || !_later[domain].Add(counter))
            return false;
        while (_consumed[domain] < uint.MaxValue - 1 && _later[domain].Remove(_consumed[domain] + 1))
            _consumed[domain]++;
        return true;
    }

    internal uint[] Snapshot() => (uint[])_consumed.Clone();
}
