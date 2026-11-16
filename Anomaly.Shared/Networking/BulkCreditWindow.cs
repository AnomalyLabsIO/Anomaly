using System;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Shared.Networking;

/// <summary>Sender accounting for absolute, disk-consumed credits. The caller owns session validation.</summary>
internal sealed class BulkCreditWindow
{
    private readonly uint _windowBytes;
    private readonly ushort _windowFrames;
    private readonly int _domainCount;
    private readonly uint[] _sent = new uint[BulkTransferProtocol.MaxDomains];
    private readonly uint[] _released = new uint[BulkTransferProtocol.MaxDomains];
    private readonly uint[] _counterBases;
    private readonly uint[] _lastCounters;
    private readonly uint[] _consumedCounters;
    private uint _creditSequence;

    internal BulkCreditWindow(uint windowBytes, ushort windowFrames, int domainCount, uint[] counterBases = null)
    {
        if (windowBytes < FileChunkMessage.MaxChunkBytes || windowBytes > BulkTransferProtocol.MaxWindowBytes)
            throw new ArgumentOutOfRangeException(nameof(windowBytes));
        if (windowFrames == 0 || windowFrames > BulkTransferProtocol.MaxWindowFrames)
            throw new ArgumentOutOfRangeException(nameof(windowFrames));
        if (domainCount <= 0 || domainCount > BulkTransferProtocol.MaxDomains)
            throw new ArgumentOutOfRangeException(nameof(domainCount));
        _windowBytes = windowBytes;
        _windowFrames = windowFrames;
        _domainCount = domainCount;
        if (counterBases != null && counterBases.Length != BulkTransferProtocol.MaxDomains)
            throw new ArgumentException("Bulk counter bases have an invalid shape.", nameof(counterBases));
        _counterBases = counterBases == null ? new uint[BulkTransferProtocol.MaxDomains] : (uint[])counterBases.Clone();
        foreach (uint counter in _counterBases)
            if (counter == uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(counterBases));
        _lastCounters = (uint[])_counterBases.Clone();
        _consumedCounters = (uint[])_counterBases.Clone();
    }

    internal ulong SentBytes { get; private set; }
    internal ulong ReleasedBytes { get; private set; }
    internal ulong OutstandingBytes => SentBytes - ReleasedBytes;
    internal uint SentFrames { get; private set; }
    internal uint ReleasedFrames { get; private set; }
    internal uint OutstandingFrames => SentFrames - ReleasedFrames;
    internal uint GetSentFrames(int domain) => _sent[ValidateDomain(domain)];
    internal uint GetReleasedFrames(int domain) => _released[ValidateDomain(domain)];
    internal uint GetOutstandingFrames(int domain) => GetSentFrames(domain) - GetReleasedFrames(domain);
    internal uint GetConsumedCounter(int domain) => _consumedCounters[ValidateDomain(domain)];
    internal uint GetLastCounter(int domain) => _lastCounters[ValidateDomain(domain)];

    internal bool CanSend(int domain, int bytes) => (uint)domain < (uint)_domainCount &&
        bytes > 0 && bytes <= FileChunkMessage.MaxChunkBytes &&
        OutstandingBytes + (ulong)bytes <= _windowBytes && OutstandingFrames < _windowFrames &&
        _sent[domain] - _released[domain] < BulkTransferProtocol.MaxOutstandingFramesPerDomain &&
        _lastCounters[domain] - _consumedCounters[domain] < BulkTransferProtocol.MaxOutstandingFramesPerDomain &&
        _lastCounters[domain] < uint.MaxValue - 1 &&
        SentFrames < uint.MaxValue && _sent[domain] < uint.MaxValue;

    internal void RecordSent(int domain, int bytes)
    {
        RecordSent(domain, bytes, GetLastCounter(domain) + 1);
    }

    internal void RecordSent(int domain, int bytes, uint counter)
    {
        if (!CanSend(domain, bytes) || counter != _lastCounters[domain] + 1)
            throw new InvalidOperationException("Bulk send exceeded credits or skipped a committed frame counter.");
        RecordFrame(domain, counter, bytes);
    }

    internal bool CanReceive(int domain, uint counter, int bytes) => (uint)domain < (uint)_domainCount &&
        counter > _consumedCounters[domain] && counter != uint.MaxValue &&
        counter - _consumedCounters[domain] <= BulkTransferProtocol.MaxOutstandingFramesPerDomain &&
        bytes > 0 && bytes <= FileChunkMessage.MaxChunkBytes &&
        OutstandingBytes + (ulong)bytes <= _windowBytes && OutstandingFrames < _windowFrames &&
        _sent[domain] - _released[domain] < BulkTransferProtocol.MaxOutstandingFramesPerDomain;

    internal void RecordReceived(int domain, uint counter, int bytes)
    {
        if (!CanReceive(domain, counter, bytes))
            throw new InvalidOperationException("Bulk receive exceeded credits or the contiguous counter span.");
        RecordFrame(domain, counter, bytes);
    }

    private void RecordFrame(int domain, uint counter, int bytes)
    {
        SentBytes = checked(SentBytes + (ulong)bytes);
        SentFrames++;
        _sent[domain]++;
        _lastCounters[domain] = Math.Max(_lastCounters[domain], counter);
    }

    internal bool TryApplyCredit(FileCreditMessage credit, out string error)
    {
        error = null;
        if (credit == null || credit.CreditSequence == 0 || credit.ReleasedFrames == null ||
            credit.ReleasedFrames.Length != BulkTransferProtocol.MaxDomains || credit.ConsumedCounters == null ||
            credit.ConsumedCounters.Length != BulkTransferProtocol.MaxDomains)
            return Reject("Bulk credit shape is invalid.", out error);
        if (credit.CreditSequence < _creditSequence) return true;

        ulong frames = 0;
        for (int domain = 0; domain < _sent.Length; domain++)
        {
            uint released = credit.ReleasedFrames[domain];
            if (released < _released[domain] || released > _sent[domain])
                return Reject("Bulk credit frame totals decreased or exceeded sent frames.", out error);
            uint consumed = credit.ConsumedCounters[domain];
            if (consumed < _consumedCounters[domain] || consumed > _lastCounters[domain] ||
                consumed - _counterBases[domain] > released ||
                credit.CreditSequence == _creditSequence && consumed != _consumedCounters[domain])
                return Reject("Bulk contiguous consumed counters decreased or exceeded verified frames.", out error);
            frames += released;
        }
        if (credit.ReleasedBytes < ReleasedBytes || credit.ReleasedBytes > SentBytes ||
            credit.ReleasedBytes < frames || credit.ReleasedBytes > frames * FileChunkMessage.MaxChunkBytes)
            return Reject("Bulk credit byte totals are invalid.", out error);
        if (credit.CreditSequence == _creditSequence &&
            (credit.ReleasedBytes != ReleasedBytes || frames != ReleasedFrames))
            return Reject("A repeated bulk credit sequence changed its totals.", out error);

        _creditSequence = credit.CreditSequence;
        ReleasedBytes = credit.ReleasedBytes;
        ReleasedFrames = checked((uint)frames);
        Array.Copy(credit.ReleasedFrames, _released, _released.Length);
        Array.Copy(credit.ConsumedCounters, _consumedCounters, _consumedCounters.Length);
        return true;
    }

    private int ValidateDomain(int domain) => (uint)domain < (uint)_domainCount
        ? domain : throw new ArgumentOutOfRangeException(nameof(domain));

    private static bool Reject(string reason, out string error) { error = reason; return false; }
}
