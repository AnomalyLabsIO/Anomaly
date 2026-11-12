using System;
using Anomaly.NetworkTransferPeers;
using Anomaly.ServerApiSmoke;

internal static class PolicyChecks
{
    private static int Main()
    {
        try
        {
            QueueCanExceedThirtySecondsWithinItsBound();
            RepeatedQueueStatusCannotRenewDeadline();
            RepeatedAdmissionCannotRenewIdleTime();
            AdmissionAcknowledgementHasThirtySecondLimit();
            ImplicitBulkAdmissionDoesNotAcceptLateQueueRenewal();
            DomainAndAggregateCreditsAreEnforced();
            FourActiveFilesAreEnforced();
            FixedConnectionDeadlineSurvivesSuccessfulProgress();
            OutOfOrderConsumedCountsCannotAdvanceCounterWindow();
            NativeDelayIsHonoredInsideFixedPreauthDeadline();
            TransientPeersDoNotReadReliableQueues();
            ReliableQueueTeardownRacesStayUnmeasured();
            RealInstrumentationFailuresRemainFailures();
            ConnectedQueuesHaveAnExactMeasuredCount();
            OptInScenariosAreBoundedAndSeparate();
            HeldPhysicalChunkRetainsItsWindowUntilDiskConsumption();
            Console.WriteLine("16 private peer policy/probe checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void TransientPeersDoNotReadReliableQueues()
    {
        var disconnected = ReliableQueueSample.Capture(64, () => false,
            () => throw new Exception("Disconnected peers must not read channel storage."),
            (channel, ordered) => throw new Exception("Disconnected peers must not read queues."));
        if (disconnected.Packets.HasValue || disconnected.Status != "peer-not-connected")
            throw new Exception("A disconnected peer was represented as an empty queue.");
        var unallocated = ReliableQueueSample.Capture(64, () => true, () => null,
            (channel, ordered) => throw new Exception("Unallocated peers must not read queues."));
        if (unallocated.Packets.HasValue || unallocated.Status != "channels-unavailable")
            throw new Exception("An unallocated channel array was represented as an empty queue.");
    }

    private static void OptInScenariosAreBoundedAndSeparate()
    {
        var normal = PeerReceivePolicy.ScenarioOptions.Parse(null, null);
        if (normal.CancelAfterBytes != 0 || normal.HoldFirstBulkChunk) throw new Exception("Scenarios were not opt-in.");
        var cancellation = PeerReceivePolicy.ScenarioOptions.Parse("1048576", "0");
        if (cancellation.CancelAfterBytes != 1048576 || cancellation.HoldFirstBulkChunk) throw new Exception("Cancellation configuration mismatch.");
        if (!PeerReceivePolicy.ScenarioOptions.Parse(null, "1").HoldFirstBulkChunk) throw new Exception("Dispatch hold configuration mismatch.");
        MustThrow<ArgumentException>(() => new PeerReceivePolicy.ScenarioOptions(1));
        MustThrow<ArgumentException>(() => new PeerReceivePolicy.ScenarioOptions(256L * 1024 * 1024));
        MustThrow<ArgumentException>(() => new PeerReceivePolicy.ScenarioOptions(16384, true));
        MustThrow<ArgumentException>(() => PeerReceivePolicy.ScenarioOptions.Parse(null, "true"));
    }

    private static void HeldPhysicalChunkRetainsItsWindowUntilDiskConsumption()
    {
        var policy = Requested(); var frames = new uint[16];
        policy.Chunk(2, 0, 1, 1); // Real transport receipt is accounted even while dispatch/disk is held.
        for (uint counter = 2; counter <= 8; counter++)
        { policy.Chunk(3, 0, 1, counter); policy.DiskWritten(0, counter, 1); frames[0]++; }
        policy.CreditSent(7, frames, policy.ConsumedCounters);
        if (policy.OutstandingBytes != 1 || policy.OutstandingFrames != 1 || policy.ConsumedCounters[0] != 0 ||
            !PeerReceivePolicy.IsBlockedDispatchHole(0, 8, 0, 7, false))
            throw new Exception("The held real frame lost its original receive slot or contiguous hole.");
        if (PeerReceivePolicy.IsBlockedDispatchHole(0, 7, 0, 6, false) || PeerReceivePolicy.IsBlockedDispatchHole(0, 8, 0, 7, true))
            throw new Exception("An incomplete or sendable span was reported as blocked.");
        MustReject(() => PeerReceivePolicy.IsBlockedDispatchHole(0, 9, 0, 7, false));
        MustReject(() => PeerReceivePolicy.IsBlockedDispatchHole(0, 8, 1, 7, false));
        MustReject(() => policy.Chunk(4, 0, 1, 9));
        policy.DiskWritten(0, 1, 1); frames[0]++;
        policy.CreditSent(8, frames, policy.ConsumedCounters); policy.Chunk(5, 0, 1, 9);
        if (policy.ConsumedCounters[0] != 8) throw new Exception("Releasing the original did not close its contiguous hole.");
        MustReject(() => policy.Check(120));
    }

    private static void ReliableQueueTeardownRacesStayUnmeasured()
    {
        bool connected = true;
        var stateRace = ReliableQueueSample.Capture(64, () => connected, () => new object[256],
            (channel, ordered) => { connected = false; throw new NullReferenceException(); });
        if (stateRace.Packets.HasValue || stateRace.Status != "peer-state-changed")
            throw new Exception("A confirmed teardown race was represented as an empty queue.");
        Array channels = new object[256];
        var storageRace = ReliableQueueSample.Capture(64, () => true, () => channels,
            (channel, ordered) => { channels = null; throw new NullReferenceException(); });
        if (storageRace.Packets.HasValue || storageRace.Status != "channels-unavailable")
            throw new Exception("Missing channel storage did not remain explicitly unmeasured.");
        connected = true;
        var finalStateRace = ReliableQueueSample.Capture(1, () => connected, () => new object[4],
            (channel, ordered) => { connected = false; return 7; });
        if (finalStateRace.Packets.HasValue) throw new Exception("A partial teardown sample was published.");
    }

    private static void RealInstrumentationFailuresRemainFailures()
    {
        MustThrow<MissingMemberException>(() => ReliableQueueSample.Capture(64, () => true,
            () => throw new MissingMemberException("_channels"), (channel, ordered) => 0));
        MustThrow<NullReferenceException>(() => ReliableQueueSample.Capture(64, () => true,
            () => new object[256], (channel, ordered) => throw new NullReferenceException("unexpected queue failure")));
        MustThrow<InvalidOperationException>(() => ReliableQueueSample.Capture(64, () => true,
            () => new object[4], (channel, ordered) => 0));
        MustThrow<InvalidOperationException>(() => ReliableQueueSample.Capture(1, () => true,
            () => new object[4], (channel, ordered) => -1));
    }

    private static void ConnectedQueuesHaveAnExactMeasuredCount()
    {
        int reads = 0;
        var measured = ReliableQueueSample.Capture(64, () => true, () => new object[256],
            (channel, ordered) => { reads++; return ordered ? 2 : 1; });
        if (reads != 128 || measured.Packets != 192 || measured.Status != "sampled")
            throw new Exception("The reliable ordered/unordered queues were not counted exactly once.");
        var empty = ReliableQueueSample.Capture(1, () => true, () => new object[4], (channel, ordered) => 0);
        if (empty.Packets != 0 || empty.Status != "sampled") throw new Exception("A measured empty queue must remain zero.");
    }

    private static void MustThrow<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("The probe suppressed an unexpected instrumentation failure: " + typeof(T).Name);
    }

    private static PeerReceivePolicy Requested()
    {
        var policy = new PeerReceivePolicy(); policy.LoginSent(0); policy.RequestSent(1, 16, new uint[16]); return policy;
    }

    private static void MustReject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        catch (TimeoutException) { return; }
        throw new Exception("A private peer policy assertion accepted an invalid operation.");
    }

    private static void QueueCanExceedThirtySecondsWithinItsBound()
    {
        var policy = Requested(); policy.Queued(2, 60000, 1); policy.Check(45); policy.Admitted(51);
        policy.BeginFile("a"); policy.Chunk(52, 0, 16384, 1);
        if (policy.State != PeerReceivePolicy.Phase.Receiving) throw new Exception("Queued transfer did not enter Receiving.");
    }

    private static void RepeatedQueueStatusCannotRenewDeadline()
    {
        var policy = Requested(); policy.Queued(2, 60000, 1); policy.Queued(50, 60000, 1);
        MustReject(() => policy.Admitted(62));
    }

    private static void RepeatedAdmissionCannotRenewIdleTime()
    {
        var policy = Requested(); policy.Admitted(2); policy.Admitted(31); MustReject(() => policy.Check(33));
    }

    private static void AdmissionAcknowledgementHasThirtySecondLimit()
    {
        var policy = Requested(); policy.Check(31); MustReject(() => policy.Queued(31.01, 60000, 1));
    }

    private static void ImplicitBulkAdmissionDoesNotAcceptLateQueueRenewal()
    {
        var policy = Requested(); policy.BeginFile("a"); policy.Chunk(2, 0, 16384, 1);
        policy.Queued(10, 60000, 1); MustReject(() => policy.Check(33));
    }

    private static void DomainAndAggregateCreditsAreEnforced()
    {
        var perDomain = Requested();
        for (int i = 0; i < 8; i++) perDomain.Chunk(2, 0, 1, (uint)i + 1);
        MustReject(() => perDomain.Chunk(2, 0, 1, 9));
        var global = Requested();
        for (int domain = 0; domain < 16; domain++)
            for (int i = 0; i < 8; i++) global.Chunk(2, domain, 16384, (uint)i + 1);
        if (global.PeakOutstandingBytes != 2097152 || global.PeakOutstandingFrames != 128 || global.PeakDomainOutstandingFrames != 8)
            throw new Exception("Declared credit bounds were not observed.");
        MustReject(() => global.Chunk(3, 0, 16384, 9));
        var frames = new uint[16]; frames[0] = 1;
        global.DiskWritten(0, 1, 16384);
        global.CreditSent(16384, frames, global.ConsumedCounters); global.Chunk(3, 0, 16384, 9);
        MustReject(() => global.CreditSent(0, new uint[16], new uint[16]));
    }

    private static void FourActiveFilesAreEnforced()
    {
        var policy = Requested();
        foreach (string key in new[] { "a", "b", "c", "d" }) policy.BeginFile(key);
        MustReject(() => policy.BeginFile("e"));
        policy.FileAcknowledged("a"); policy.BeginFile("e");
        if (policy.PeakActiveFiles != 4) throw new Exception("Four-file receive bound missing.");
    }

    private static void FixedConnectionDeadlineSurvivesSuccessfulProgress()
    {
        var policy = Requested(); policy.Queued(2, 60000, 1); policy.Admitted(51);
        policy.BeginFile("a"); policy.Chunk(52, 0, 1, 1); policy.DiskWritten(0, 1, 1);
        var frames = new uint[16]; frames[0] = 1; policy.CreditSent(1, frames, policy.ConsumedCounters);
        policy.FileAcknowledged("a"); policy.Acquired(53);
        policy.Check(119.99); MustReject(() => policy.Check(120));
    }

    private static void OutOfOrderConsumedCountsCannotAdvanceCounterWindow()
    {
        var policy = Requested(); var frames = new uint[16];
        for (uint counter = 2; counter <= 8; counter++)
        { policy.Chunk(2, 0, 1, counter); policy.DiskWritten(0, counter, 1); frames[0]++; }
        policy.CreditSent(7, frames, policy.ConsumedCounters);
        if (policy.ConsumedCounters[0] != 0) throw new Exception("Out-of-order counts advanced a contiguous counter watermark.");
        MustReject(() => policy.Chunk(3, 0, 1, 9));
        policy.Chunk(3, 0, 1, 1); policy.DiskWritten(0, 1, 1); frames[0]++;
        policy.CreditSent(8, frames, policy.ConsumedCounters);
        if (policy.ConsumedCounters[0] != 8) throw new Exception("Contiguous disk consumption did not close the counter hole.");
        policy.Chunk(4, 0, 1, 9);
    }

    private static void NativeDelayIsHonoredInsideFixedPreauthDeadline()
    {
        if (PeerReceivePolicy.BoundPreauthRetry(1, 30, 3) != 4) throw new Exception("Native DelayTime was not honored.");
        MustReject(() => PeerReceivePolicy.BoundPreauthRetry(28, 30, 3));
        MustReject(() => PeerReceivePolicy.BoundPreauthRetry(1, 30, 0));
    }
}
