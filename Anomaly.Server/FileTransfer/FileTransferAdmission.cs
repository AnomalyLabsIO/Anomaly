using System;
using System.Collections.Generic;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Server.FileTransfer;

/// <summary>Bounded FIFO capacity planning. Network/storage stalls remain subject to the absolute content deadline.</summary>
internal static class FileTransferAdmission
{
    internal const int MaximumActiveTransfers = 8;
    internal const long MaximumReservedBytes = 512L * 1024L * 1024L;
    internal const double ActivationReserveSeconds = 10;
    internal const double VerificationReserveSeconds = 2;
    internal const int UnmeasuredRoundTripTimeMilliseconds = 100;
    private const double FlightWindowHeadroom = .75;

    internal readonly struct Work
    {
        internal readonly long ReservedBytes, RemainingBytes, RemainingAccountedBytes;
        internal readonly double MaximumAccountedBytesPerSecond;
        internal readonly double RemainingSeconds, QueueRemainingSeconds;
        internal readonly int RemainingFiles, PendingCompletions, RoundTripTimeMilliseconds;
        internal Work(long reservedBytes, long remainingBytes, long remainingAccountedBytes, double maximumAccountedBytesPerSecond,
            double remainingSeconds, int remainingFiles = 0, int pendingCompletions = 0, int roundTripTimeMilliseconds = 0,
            double queueRemainingSeconds = double.MaxValue)
        {
            ReservedBytes = reservedBytes; RemainingBytes = remainingBytes; RemainingAccountedBytes = remainingAccountedBytes;
            MaximumAccountedBytesPerSecond = maximumAccountedBytesPerSecond;
            RemainingSeconds = remainingSeconds; QueueRemainingSeconds = queueRemainingSeconds;
            RemainingFiles = remainingFiles; PendingCompletions = pendingCompletions; RoundTripTimeMilliseconds = roundTripTimeMilliseconds;
        }

        internal double TransactionFloorSeconds
        {
            get
            {
                int rtt = RoundTripTimeMilliseconds == 0 ? UnmeasuredRoundTripTimeMilliseconds : RoundTripTimeMilliseconds;
                // Without proven per-file ACK timing, an in-flight first wave
                // may still owe a full RTT. Keep it in the unverified count.
                double pipeline = Math.Ceiling(RemainingFiles / (double)BulkTransferProtocol.MaxFilesInFlight) * rtt / 1000d;
                double completions = Math.Max(0, PendingCompletions - FileCompletionPacer.Burst) /
                    (double)FileCompletionPacer.MessagesPerSecond;
                return Math.Max(pipeline, completions);
            }
        }
    }

    // Call for each file's unsent chunk-aligned remainder, never for the sum of
    // several files. Every distinct tail pays its own rounding and frame cost.
    internal static long AccountedFileRemainder(long remainingBytes)
    {
        if (remainingBytes < 0 || remainingBytes > FileManifestMessage.MaxAggregateBytes)
            throw new ArgumentOutOfRangeException(nameof(remainingBytes));
        long fullChunks = remainingBytes / FileChunkMessage.MaxChunkBytes;
        int tail = (int)(remainingBytes % FileChunkMessage.MaxChunkBytes);
        return checked(fullChunks * BulkTransferBudget.Charge(FileChunkMessage.MaxChunkBytes) +
            (tail == 0 ? 0 : BulkTransferBudget.Charge(tail)));
    }

    /// <summary>
    /// Caps the configured allowance by independent native flight windows. This
    /// is an admission estimate, not guaranteed goodput: RTT/storage/loss can change.
    /// Mtu is the peer's effective MTU; RoundTripTime is full RTT, unlike Ping.
    /// </summary>
    internal static bool TryEstimateAccountedRate(BulkTransferBudget budget, int domains, int mtu,
        int roundTripTimeMilliseconds, out double maximumAccountedBytesPerSecond)
    {
        maximumAccountedBytesPerSecond = 0;
        if (budget == null || domains <= 0 || domains > 16 || mtu <= 10 || mtu > ushort.MaxValue || roundTripTimeMilliseconds < 0)
            return false;
        int rtt = roundTripTimeMilliseconds == 0 ? UnmeasuredRoundTripTimeMilliseconds : roundTripTimeMilliseconds;
        // The stock stack has 64 packet slots per reliable lane and ten bytes of
        // fragmented-packet headers. Do not extrapolate a jumbo override beyond
        // the inspected stock ceiling. Reserve ACK/polling/framing headroom.
        double payloadRate = domains * 64d * (Math.Min(mtu, 1432) - 10) * 1000d / Math.Max(1, rtt) * FlightWindowHeadroom;
        double accountedRate = payloadRate * BulkTransferBudget.Charge(FileChunkMessage.MaxChunkBytes) / FileChunkMessage.MaxChunkBytes;
        maximumAccountedBytesPerSecond = Math.Min(budget.ClientBytesPerSecond, accountedRate);
        return maximumAccountedBytesPerSecond > 0;
    }

    internal static bool TryPlan(BulkTransferBudget budget, IList<Work> active, IList<Work> queued,
        Work requested, double remainingSeconds, out double queueLimitSeconds,
        double schedulerAccountedBytesPerSecond = double.MaxValue, int targetQueuePosition = -1)
    {
        queueLimitSeconds = 0;
        if (!Valid(requested) || schedulerAccountedBytesPerSecond <= 0 ||
            double.IsNaN(schedulerAccountedBytesPerSecond) || double.IsInfinity(schedulerAccountedBytesPerSecond) ||
            double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds) ||
            targetQueuePosition < -1 || targetQueuePosition > queued.Count) return false;
        remainingSeconds = Math.Min(remainingSeconds, requested.RemainingSeconds);
        double aggregateRate = Math.Min(budget.GlobalBytesPerSecond, schedulerAccountedBytesPerSecond);
        double ownRate = Math.Min(aggregateRate, Math.Min(budget.ClientBytesPerSecond, requested.MaximumAccountedBytesPerSecond));
        double ownSeconds = Math.Max(requested.RemainingAccountedBytes / ownRate, requested.TransactionFloorSeconds) +
            VerificationReserveSeconds;
        queueLimitSeconds = Math.Min(FileTransferStatusMessage.MaxQueueWaitMilliseconds / 1000d,
            remainingSeconds - ownSeconds - ActivationReserveSeconds);
        if (queueLimitSeconds < 0) return false;
        queueLimitSeconds = Math.Min(queueLimitSeconds, requested.QueueRemainingSeconds);

        var running = new List<SimulatedWork>();
        long reserved = 0;
        foreach (var work in active)
        {
            if (!Valid(work)) return false;
            var simulated = new SimulatedWork(work, budget, work.RemainingSeconds, double.MaxValue);
            simulated.Start(0); running.Add(simulated);
            reserved += work.ReservedBytes;
        }
        var waiting = new Queue<SimulatedWork>();
        var target = new SimulatedWork(requested, budget, remainingSeconds, queueLimitSeconds);
        if (targetQueuePosition < 0) targetQueuePosition = queued.Count;
        for (int index = 0; index <= queued.Count; index++)
        {
            if (index == targetQueuePosition) waiting.Enqueue(target);
            if (index == queued.Count) break;
            var work = queued[index];
            if (!Valid(work)) return false;
            waiting.Enqueue(new SimulatedWork(work, budget, work.RemainingSeconds, work.QueueRemainingSeconds));
        }
        double elapsed = 0;
        for (int events = 0; events < 82; events++)
        {
            while (waiting.Count != 0 && running.Count < MaximumActiveTransfers &&
                reserved + waiting.Peek().ReservedBytes <= MaximumReservedBytes)
            {
                var next = waiting.Dequeue();
                if (elapsed > next.QueueDeadline) return false;
                next.Start(elapsed); running.Add(next); reserved += next.ReservedBytes;
            }
            if (running.Count == 0) return false;
            var caps = new double[running.Count];
            for (int index = 0; index < running.Count; index++)
                caps[index] = running[index].DataComplete ? 0 : running[index].RateCap;
            double[] rates = FairRates(caps, aggregateRate);
            double advance = double.MaxValue;
            for (int index = 0; index < running.Count; index++)
                advance = Math.Min(advance, running[index].DataComplete
                    ? Math.Max(0, running[index].CompletionAt - elapsed)
                    : running[index].RemainingCharge / rates[index]);
            elapsed += advance;
            // Every admitted owner keeps its own absolute deadline, including
            // queued owners behind the target. Finishing the newcomer first is
            // not proof that the older promises still fit.
            foreach (var work in running)
                if (elapsed + ActivationReserveSeconds > work.Deadline) return false;
            foreach (var work in waiting)
                if (elapsed > work.QueueDeadline || elapsed + ActivationReserveSeconds > work.Deadline) return false;
            for (int index = running.Count - 1; index >= 0; index--)
            {
                var work = running[index];
                if (!work.DataComplete)
                {
                    work.RemainingCharge = Math.Max(0, work.RemainingCharge - advance * rates[index]);
                    if (work.RemainingCharge <= .001) work.FinishData(elapsed);
                }
                if (!work.DataComplete || work.CompletionAt > elapsed + .000001) continue;
                reserved -= work.ReservedBytes;
                running.RemoveAt(index);
            }
            if (running.Count == 0 && waiting.Count == 0) return true;
        }
        return false;
    }

    private static bool Valid(Work work) => work.ReservedBytes > 0 && work.ReservedBytes <= FileManifestMessage.MaxAggregateBytes &&
        work.RemainingBytes >= 0 && work.RemainingBytes <= work.ReservedBytes &&
        work.RemainingAccountedBytes >= AccountedFileRemainder(work.RemainingBytes) &&
        work.RemainingAccountedBytes <= AccountedFileRemainder(work.RemainingBytes) +
            (work.RemainingBytes == 0 ? 0 : (FileManifestMessage.MaxFiles - 1) * 257L) &&
        work.RemainingFiles >= 0 && work.RemainingFiles <= FileManifestMessage.MaxFiles &&
        work.PendingCompletions >= 0 && work.PendingCompletions <= work.RemainingFiles && work.RoundTripTimeMilliseconds >= 0 &&
        work.RemainingSeconds > 0 && !double.IsNaN(work.RemainingSeconds) && !double.IsInfinity(work.RemainingSeconds) &&
        work.QueueRemainingSeconds >= 0 && !double.IsNaN(work.QueueRemainingSeconds) && !double.IsInfinity(work.QueueRemainingSeconds) &&
        work.MaximumAccountedBytesPerSecond > 0 &&
        !double.IsNaN(work.MaximumAccountedBytesPerSecond) && !double.IsInfinity(work.MaximumAccountedBytesPerSecond);

    // Max-min sharing releases a slow peer's unused allowance to other peers.
    // All values are accounted upload bytes/second, matching the token budget.
    internal static double[] FairRates(double[] caps, double globalBytesPerSecond)
    {
        var rates = new double[caps.Length];
        var pending = new List<int>();
        for (int index = 0; index < caps.Length; index++) pending.Add(index);
        double remaining = globalBytesPerSecond;
        while (pending.Count != 0)
        {
            double share = remaining / pending.Count;
            bool capped = false;
            for (int position = pending.Count - 1; position >= 0; position--)
            {
                int index = pending[position];
                if (caps[index] > share) continue;
                rates[index] = caps[index]; remaining -= rates[index];
                pending.RemoveAt(position); capped = true;
            }
            if (capped) continue;
            foreach (int index in pending) rates[index] = share;
            break;
        }
        return rates;
    }

    private sealed class SimulatedWork
    {
        internal readonly long ReservedBytes;
        internal readonly double Deadline, QueueDeadline;
        internal readonly double RateCap;
        private readonly double _transactionFloorSeconds;
        private double _transactionNotBefore;
        internal double RemainingCharge;
        internal bool DataComplete;
        internal double CompletionAt;
        internal SimulatedWork(Work work, BulkTransferBudget budget, double deadline, double queueDeadline)
        {
            ReservedBytes = work.ReservedBytes; Deadline = deadline; QueueDeadline = queueDeadline;
            RateCap = Math.Min(budget.ClientBytesPerSecond, work.MaximumAccountedBytesPerSecond);
            RemainingCharge = work.RemainingAccountedBytes;
            _transactionFloorSeconds = work.TransactionFloorSeconds;
        }

        internal void Start(double elapsed)
        {
            _transactionNotBefore = elapsed + _transactionFloorSeconds;
            if (RemainingCharge == 0) FinishData(elapsed);
        }

        internal void FinishData(double elapsed)
        {
            DataComplete = true;
            CompletionAt = Math.Max(elapsed, _transactionNotBefore) + VerificationReserveSeconds;
        }
    }
}
