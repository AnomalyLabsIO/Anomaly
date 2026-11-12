using System;
using System.Collections.Generic;

namespace Anomaly.NetworkTransferPeers;

// Independent fixture assertions. Credits describe data consumed by disk, while
// outstanding bounds use the last credit actually handed to the transport.
internal sealed class PeerReceivePolicy
{
    internal sealed class ScenarioOptions
    {
        internal readonly long CancelAfterBytes;
        internal readonly bool HoldFirstBulkChunk;
        internal ScenarioOptions(long cancelAfterBytes = 0, bool holdFirstBulkChunk = false)
        {
            if (cancelAfterBytes < 0 || cancelAfterBytes >= 256L * 1024 * 1024 ||
                cancelAfterBytes > 0 && cancelAfterBytes < 16 * 1024 || cancelAfterBytes > 0 && holdFirstBulkChunk)
                throw new ArgumentException("Use one bounded cancellation or dispatch-hole scenario per run.");
            CancelAfterBytes = cancelAfterBytes; HoldFirstBulkChunk = holdFirstBulkChunk;
        }
        internal static ScenarioOptions Parse(string cancellation, string hold)
        {
            if (!string.IsNullOrEmpty(hold) && hold != "0" && hold != "1")
                throw new ArgumentException("ANOMALY_TRANSFER_PEER_HOLD_FIRST_BULK must be 0 or 1.");
            return new ScenarioOptions(string.IsNullOrEmpty(cancellation) ? 0 : long.Parse(cancellation), hold == "1");
        }
    }

    internal static bool IsBlockedDispatchHole(uint counterBase, uint lastCounter, uint consumedCounter,
        uint releasedFrames, bool canSend)
    {
        if (lastCounter < counterBase || lastCounter - counterBase > 8 || consumedCounter != counterBase || releasedFrames > 7)
            throw new InvalidOperationException("The held original chunk was credited or its bounded counter span was exceeded.");
        return lastCounter - counterBase == 8 && releasedFrames == 7 && !canSend;
    }

    internal enum Phase { BeforeLogin, AwaitingOffer, AwaitingAdmission, Queued, Receiving, Acquired }
    private const int ChunkBytes = 16 * 1024;
    private readonly uint[] _receivedFrames = new uint[16], _creditedFrames = new uint[16];
    private readonly uint[] _diskFrames = new uint[16];
    private readonly uint[] _bases = new uint[16], _consumedCounters = new uint[16], _creditedCounters = new uint[16];
    private readonly HashSet<uint>[] _receivedCounters = new HashSet<uint>[16], _writtenCounters = new HashSet<uint>[16];
    private readonly HashSet<string> _activeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private double _deadline, _lastProgress, _queueDeadline;
    private ulong _receivedBytes, _creditedBytes;
    private ulong _diskBytes;
    private uint _frameCount, _creditedFrameCount;
    private int _domains;

    internal Phase State { get; private set; }
    internal ulong OutstandingBytes => _receivedBytes - _creditedBytes;
    internal uint OutstandingFrames => _frameCount - _creditedFrameCount;
    internal ulong PeakOutstandingBytes { get; private set; }
    internal uint PeakOutstandingFrames { get; private set; }
    internal uint PeakDomainOutstandingFrames { get; private set; }
    internal uint PeakCounterSpan { get; private set; }
    internal int PeakActiveFiles { get; private set; }
    internal uint[] ConsumedCounters => (uint[])_consumedCounters.Clone();

    internal static double BoundPreauthRetry(double now, double deadline, byte delaySeconds)
    {
        if (delaySeconds == 0 || double.IsNaN(now) || double.IsInfinity(now) || now + delaySeconds >= deadline)
            throw new TimeoutException("Server delay cannot fit the fixture's fixed preauthentication deadline.");
        return now + delaySeconds;
    }

    internal PeerReceivePolicy()
    {
        for (int domain = 0; domain < 16; domain++)
        { _receivedCounters[domain] = new HashSet<uint>(); _writtenCounters[domain] = new HashSet<uint>(); }
    }

    internal void LoginSent(double now)
    {
        if (State != Phase.BeforeLogin) throw new InvalidOperationException("Repeated fixture login.");
        State = Phase.AwaitingOffer;
        _deadline = now + 120;
        _lastProgress = now;
    }

    internal void RequestSent(double now, int domains, uint[] counterBases)
    {
        Check(now);
        if (State != Phase.AwaitingOffer || domains < 1 || domains > 16 || counterBases == null || counterBases.Length != 16)
            throw new InvalidOperationException("Invalid fixture request phase/domains.");
        _domains = domains;
        foreach (uint counter in counterBases)
            if (counter == uint.MaxValue) throw new InvalidOperationException("Reserved fixture counter base.");
        Array.Copy(counterBases, _bases, 16); Array.Copy(counterBases, _consumedCounters, 16); Array.Copy(counterBases, _creditedCounters, 16);
        State = Phase.AwaitingAdmission;
        _lastProgress = now;
    }

    internal void Queued(double now, ushort waitMilliseconds, byte position)
    {
        Check(now);
        if (waitMilliseconds == 0 || waitMilliseconds > 60000 || position == 0 || position > 32)
            throw new InvalidOperationException("Invalid bounded fixture queue status.");
        if (State == Phase.Receiving || State == Phase.Queued) return;
        if (State != Phase.AwaitingAdmission) throw new InvalidOperationException("Queue status without request.");
        State = Phase.Queued;
        _queueDeadline = now + waitMilliseconds / 1000d;
    }

    internal void Admitted(double now)
    {
        Check(now);
        if (State == Phase.Receiving) return; // A repeated status cannot renew idle time.
        if (State != Phase.AwaitingAdmission && State != Phase.Queued)
            throw new InvalidOperationException("Admission without live request.");
        State = Phase.Receiving;
        _lastProgress = now;
    }

    internal void BeginFile(string key)
    {
        if (_activeFiles.Contains(key)) return;
        if (_activeFiles.Count >= 4) throw new InvalidOperationException("Server exceeded four active receive files.");
        _activeFiles.Add(key);
        PeakActiveFiles = Math.Max(PeakActiveFiles, _activeFiles.Count);
    }

    internal void Chunk(double now, int domain, int bytes, uint counter)
    {
        Check(now);
        if (State != Phase.AwaitingAdmission && State != Phase.Queued && State != Phase.Receiving)
            throw new InvalidOperationException("Chunk without live request.");
        if (domain < 0 || domain >= _domains || bytes <= 0 || bytes > ChunkBytes ||
            OutstandingBytes + (ulong)bytes > 2UL * 1024 * 1024 || OutstandingFrames >= 128 ||
            _receivedFrames[domain] - _creditedFrames[domain] >= 8 || counter <= _creditedCounters[domain] ||
            counter - _creditedCounters[domain] > 8 || !_receivedCounters[domain].Add(counter))
            throw new InvalidOperationException("Server exceeded the granted byte/frame/domain receive window.");
        _receivedBytes += (ulong)bytes; _receivedFrames[domain]++; _frameCount++;
        PeakOutstandingBytes = Math.Max(PeakOutstandingBytes, OutstandingBytes);
        PeakOutstandingFrames = Math.Max(PeakOutstandingFrames, OutstandingFrames);
        PeakDomainOutstandingFrames = Math.Max(PeakDomainOutstandingFrames, _receivedFrames[domain] - _creditedFrames[domain]);
        PeakCounterSpan = Math.Max(PeakCounterSpan, counter - _creditedCounters[domain]);
        State = Phase.Receiving; // A bulk lane may overtake admission control.
        _lastProgress = now;
    }

    internal void DiskWritten(int domain, uint counter, int bytes)
    {
        if ((uint)domain >= (uint)_domains || bytes <= 0 || bytes > ChunkBytes || _diskBytes + (ulong)bytes > _receivedBytes ||
            counter <= _consumedCounters[domain] ||
            !_receivedCounters[domain].Contains(counter) || !_writtenCounters[domain].Add(counter))
            throw new InvalidOperationException("Invalid or repeated fixture disk-consumed counter.");
        _diskBytes += (ulong)bytes; _diskFrames[domain]++;
        while (_consumedCounters[domain] < uint.MaxValue && _writtenCounters[domain].Remove(_consumedCounters[domain] + 1))
            _consumedCounters[domain]++;
    }

    internal void FirstCompletion(double now)
    {
        Check(now);
        if (State != Phase.AwaitingAdmission && State != Phase.Queued && State != Phase.Receiving)
            throw new InvalidOperationException("File completion without live request.");
        State = Phase.Receiving;
        _lastProgress = now;
    }

    internal void CreditSent(ulong bytes, uint[] frames, uint[] counters)
    {
        if (frames == null || frames.Length != 16 || counters == null || counters.Length != 16 || bytes != _diskBytes || bytes < _creditedBytes || bytes > _receivedBytes)
            throw new InvalidOperationException("Invalid fixture consumed credit bytes.");
        uint count = 0;
        for (int domain = 0; domain < frames.Length; domain++)
        {
            if (frames[domain] != _diskFrames[domain] || frames[domain] < _creditedFrames[domain] || frames[domain] > _receivedFrames[domain] ||
                counters[domain] != _consumedCounters[domain] || counters[domain] < _creditedCounters[domain])
                throw new InvalidOperationException("Invalid fixture consumed credit frames.");
            count += frames[domain];
        }
        _creditedBytes = bytes; _creditedFrameCount = count;
        Array.Copy(frames, _creditedFrames, frames.Length);
        Array.Copy(counters, _creditedCounters, counters.Length);
        for (int domain = 0; domain < _receivedCounters.Length; domain++)
        {
            uint consumed = counters[domain];
            _receivedCounters[domain].RemoveWhere(counter => counter <= consumed);
        }
    }

    internal void FileAcknowledged(string key)
    {
        if (!_activeFiles.Remove(key)) throw new InvalidOperationException("ACK without an active fixture file.");
    }

    internal void Acquired(double now)
    {
        Check(now);
        if (_activeFiles.Count != 0 || State != Phase.Receiving || OutstandingBytes != 0 || OutstandingFrames != 0)
            throw new InvalidOperationException("Fixture acquisition completed with active files.");
        for (int domain = 0; domain < _domains; domain++)
            if (_writtenCounters[domain].Count != 0 || _receivedCounters[domain].Count != 0 ||
                (ulong)_consumedCounters[domain] - _bases[domain] != _receivedFrames[domain])
                throw new InvalidOperationException("Fixture acquisition completed with an unconsumed domain-counter hole.");
        State = Phase.Acquired;
    }

    internal void Check(double now)
    {
        if (double.IsNaN(now) || double.IsInfinity(now)) throw new InvalidOperationException("Invalid fixture time.");
        if (State == Phase.BeforeLogin) return;
        if (now >= _deadline) throw new TimeoutException("Fixture's fixed 120-second content connection deadline expired.");
        if (State == Phase.Queued && now >= _queueDeadline)
            throw new TimeoutException("Fixture's bounded queued admission deadline expired.");
        if ((State == Phase.AwaitingAdmission || State == Phase.Receiving) && now - _lastProgress > 30)
            throw new TimeoutException("Fixture saw no admission/progress within the phase's 30-second idle deadline.");
    }
}
