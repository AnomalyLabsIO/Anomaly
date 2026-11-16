namespace Anomaly.Shared.Networking;

/// <summary>Separate authenticated domains for striped file chunks; scoped to the crypto session.</summary>
internal sealed class BulkReplayState
{
    private readonly AnomalyReplayState[] _domains = new AnomalyReplayState[BulkTransferProtocol.MaxDomains];
    private readonly uint[] _committedTx = new uint[BulkTransferProtocol.MaxDomains];

    internal BulkReplayState()
    {
        for (int index = 0; index < _domains.Length; index++) _domains[index] = new AnomalyReplayState();
    }

    internal uint PeekNextTxCounter(int domain)
    {
        if ((uint)domain >= (uint)_domains.Length) return 0;
        lock (_committedTx)
            return _committedTx[domain] >= uint.MaxValue - 1 ? 0 : _committedTx[domain] + 1;
    }

    internal bool TryCommitTxCounter(int domain, uint counter)
    {
        if ((uint)domain >= (uint)_domains.Length || counter == 0 || counter == uint.MaxValue) return false;
        lock (_committedTx)
        {
            if (_committedTx[domain] != counter - 1) return false;
            _committedTx[domain] = counter;
            return true;
        }
    }

    internal uint[] GetTxCounterBases()
    {
        lock (_committedTx) return (uint[])_committedTx.Clone();
    }

    internal uint NextTxCounter(int domain)
    {
        lock (_committedTx)
        {
            uint counter = PeekNextTxCounter(domain);
            return TryCommitTxCounter(domain, counter) ? counter : 0;
        }
    }

    internal AnomalyReplayState.RxResult TryAcceptRx(int domain, uint counter, out bool shouldLog)
    {
        shouldLog = false;
        return (uint)domain < (uint)_domains.Length
            ? _domains[domain].TryAcceptRx(MessageChannel.ReliableUnordered, counter, out shouldLog)
            : AnomalyReplayState.RxResult.RejectedInvalid;
    }
}
