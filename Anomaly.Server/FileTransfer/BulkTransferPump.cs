using System;
using System.Collections.Generic;
using System.Diagnostics;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Server.FileTransfer;

internal enum BulkSendResult { Sent, Backpressured, Failed }

internal interface IBulkTransferSource
{
    // A blocked source retains ownership of its next frame. No credit or rate
    // budget is spent until the transport has accepted that exact frame.
    BulkPumpResult TrySendNext(int maximumBytes, out int sentBytes);
}

internal enum BulkPumpResult { Sent, Blocked, Finished }

/// <summary>Main-thread fair scheduler. Disk workers never enter the transport.</summary>
internal sealed class BulkTransferPump
{
    internal const int MaximumBytesPerUpdate = 512 * 1024;
    internal const int MaximumFramesPerUpdate = 32;
    internal const double MaximumSecondsPerUpdate = .002;
    private readonly Func<double> _clock;
    private readonly LinkedList<Entry> _ready = new LinkedList<Entry>();
    private readonly Dictionary<IBulkTransferSource, Entry> _entries = new Dictionary<IBulkTransferSource, Entry>();
    private readonly BulkSchedulerCapacity _capacity = new BulkSchedulerCapacity();
    private double _globalTokens = MaximumBytesPerUpdate;
    private double _lastRefill;
    private BulkTransferBudget _budget = BulkTransferBudget.Default;

    internal BulkTransferPump(Func<double> clock = null)
    {
        _clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        _lastRefill = _clock();
    }

    internal int Count => _entries.Count;
    internal int QueuedCount => _ready.Count;
    internal BulkTransferBudget Budget => _budget;
    internal BulkSchedulerCapacity.Estimate AdmissionCapacity => _capacity.GetEstimate(_budget);

    internal void Configure(BulkTransferBudget budget)
    {
        if (_entries.Count != 0) throw new InvalidOperationException("Bulk upload policy cannot change during active transfers.");
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
        Reset();
    }

    internal void Add(IBulkTransferSource source)
    {
        if (_entries.ContainsKey(source)) return;
        if (_entries.Count == 0) _capacity.Inactive();
        var entry = new Entry(source, _clock(), BulkTransferBudget.Burst(_budget.ClientBytesPerSecond));
        _entries.Add(source, entry);
        entry.Node = _ready.AddLast(entry);
    }

    internal void Remove(IBulkTransferSource source)
    {
        if (_entries.TryGetValue(source, out var entry)) Remove(entry);
    }

    private void Remove(Entry entry)
    {
        if (!_entries.TryGetValue(entry.Source, out var current) || !ReferenceEquals(entry, current)) return;
        _entries.Remove(entry.Source);
        if (entry.Node != null) { _ready.Remove(entry.Node); entry.Node = null; }
        if (_entries.Count == 0) _capacity.Inactive();
    }

    internal void Reset()
    {
        _ready.Clear();
        _entries.Clear();
        _globalTokens = BulkTransferBudget.Burst(_budget.GlobalBytesPerSecond);
        _lastRefill = _clock();
        _capacity.Reset();
    }

    internal void Tick()
    {
        if (_entries.Count == 0) { _capacity.Inactive(); return; }
        double started = _clock();
        _globalTokens = Refill(_globalTokens, started - _lastRefill,
            _budget.GlobalBytesPerSecond, BulkTransferBudget.Burst(_budget.GlobalBytesPerSecond));
        _lastRefill = started;
        int bytes = 0, frames = 0, blocked = 0;
        long accounted = 0;
        double successfulSendSeconds = 0;
        bool allReady = true;
        while (_ready.Count != 0 && frames < MaximumFramesPerUpdate &&
            bytes < MaximumBytesPerUpdate && _clock() - started < MaximumSecondsPerUpdate)
        {
            var entry = _ready.First.Value;
            _ready.RemoveFirst();
            entry.Node = null;
            if (!_entries.TryGetValue(entry.Source, out var current) || !ReferenceEquals(entry, current))
                continue;
            double now = _clock();
            entry.Tokens = Refill(entry.Tokens, now - entry.LastRefill,
                _budget.ClientBytesPerSecond, BulkTransferBudget.Burst(_budget.ClientBytesPerSecond));
            entry.LastRefill = now;
            int allowance = Math.Min(MaximumBytesPerUpdate - bytes,
                BulkTransferBudget.PayloadAllowance(Math.Min(_globalTokens, entry.Tokens)));
            double operationStarted = _clock();
            BulkPumpResult result = allowance <= 0 ? BulkPumpResult.Blocked :
                entry.Source.TrySendNext(allowance, out entry.LastBytes);
            double operationSeconds = Math.Max(0, _clock() - operationStarted);
            if (result == BulkPumpResult.Sent)
            {
                if (entry.LastBytes <= 0 || entry.LastBytes > allowance)
                    throw new InvalidOperationException("Bulk source exceeded its scheduler byte allowance.");
                int charge = BulkTransferBudget.Charge(entry.LastBytes);
                successfulSendSeconds += operationSeconds;
                accounted += charge;
                _capacity.ObserveSend(entry.LastBytes, charge, operationSeconds);
                if (entry.LastBytes < FileChunkMessage.MaxChunkBytes / 2) allReady = false;
                _globalTokens -= charge;
                entry.Tokens -= charge;
                bytes += entry.LastBytes;
                frames++;
                blocked = 0;
            }
            else if (result == BulkPumpResult.Finished)
            {
                allReady = false;
                Remove(entry);
            }
            else
            {
                allReady = false;
                blocked++;
            }

            if (_entries.TryGetValue(entry.Source, out current) && ReferenceEquals(entry, current))
                entry.Node = _ready.AddLast(entry);
            if (_entries.Count == 0 || blocked >= _entries.Count) break;
        }
        double finished = _clock();
        if (_entries.Count == 0) { _capacity.Inactive(); return; }
        _capacity.ObserveTick(started, finished, successfulSendSeconds, accounted,
            allReady && frames > 0 && _entries.Count != 0 && frames < MaximumFramesPerUpdate &&
            bytes < MaximumBytesPerUpdate && finished - started >= MaximumSecondsPerUpdate);
    }

    private static double Refill(double tokens, double elapsed, double perSecond, double burst) =>
        Math.Min(burst, tokens + Math.Max(0, elapsed) * perSecond);

    private sealed class Entry
    {
        internal readonly IBulkTransferSource Source;
        internal LinkedListNode<Entry> Node;
        internal double Tokens;
        internal double LastRefill;
        internal int LastBytes;
        internal Entry(IBulkTransferSource source, double now, double tokens) { Source = source; LastRefill = now; Tokens = tokens; }
    }
}
