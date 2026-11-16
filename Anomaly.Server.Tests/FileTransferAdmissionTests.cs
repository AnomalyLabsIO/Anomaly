using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class FileTransferAdmissionTests
{
    private const long LargeFixture = 267691541;

    [Theory]
    [InlineData(39d, false)]
    [InlineData(70d, true)]
    public void StaggeredArrivalPreservesTheAlreadyAdmittedTransfersOwnDeadline(double incumbentSeconds, bool allowed)
    {
        var budget = new BulkTransferBudget(8 * 1024 * 1024, 8 * 1024 * 1024, 20);
        const long bytes = 128L * 1024 * 1024;
        var incumbent = new FileTransferAdmission.Work(bytes, bytes, FileTransferAdmission.AccountedFileRemainder(bytes),
            budget.ClientBytesPerSecond, incumbentSeconds, 4, 4, 100);
        var newcomer = new FileTransferAdmission.Work(bytes, bytes, FileTransferAdmission.AccountedFileRemainder(bytes),
            budget.ClientBytesPerSecond, 110, 4, 4, 100);
        Assert.True(FileTransferAdmission.TryPlan(budget, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), incumbent, incumbentSeconds, out _));
        Assert.Equal(allowed, FileTransferAdmission.TryPlan(budget, new[] { incumbent },
            Array.Empty<FileTransferAdmission.Work>(), newcomer, 110, out _));
    }

    [Fact]
    public void FinishingTheNewcomerFirstDoesNotHideALaterIncumbentDeadlineMiss()
    {
        var budget = new BulkTransferBudget(8 * 1024 * 1024, 8 * 1024 * 1024, 20);
        const long bytes = 128L * 1024 * 1024;
        var incumbent = new FileTransferAdmission.Work(bytes, bytes, FileTransferAdmission.AccountedFileRemainder(bytes),
            budget.ClientBytesPerSecond, 37.4, 4, 4, 100);
        Assert.True(FileTransferAdmission.TryPlan(budget, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), incumbent, 37.4, out _));
        Assert.False(FileTransferAdmission.TryPlan(budget, new[] { incumbent },
            Array.Empty<FileTransferAdmission.Work>(), Fresh(1024 * 1024), 110, out _));
    }

    [Fact]
    public void ReadmittingTheQueueHeadStillProtectsAnAlreadyPromisedFollower()
    {
        var budget = new BulkTransferBudget(8 * 1024 * 1024, 8 * 1024 * 1024, 20);
        const long bytes = 128L * 1024 * 1024;
        var follower = new FileTransferAdmission.Work(bytes, bytes, FileTransferAdmission.AccountedFileRemainder(bytes),
            budget.ClientBytesPerSecond, 39, 4, 4, 100, queueRemainingSeconds: 10);
        Assert.False(FileTransferAdmission.TryPlan(budget, Array.Empty<FileTransferAdmission.Work>(), new[] { follower },
            Fresh(bytes), 110, out _, targetQueuePosition: 0));
    }

    [Theory]
    [InlineData(2d, false)]
    [InlineData(3d, true)]
    public void ExistingQueueWaitIsNotRenewedByAYoungerArrival(double queueSeconds, bool allowed)
    {
        var budget = BulkTransferBudget.Default;
        var active = Enumerable.Range(0, 8).Select(_ => new FileTransferAdmission.Work(64 * 1024 * 1024, 0, 0,
            budget.ClientBytesPerSecond, 120, 4, 0, 1000)).ToArray();
        var queued = new FileTransferAdmission.Work(1, 1, 258, budget.ClientBytesPerSecond, 100,
            1, 1, 100, queueRemainingSeconds: queueSeconds);
        Assert.Equal(allowed, FileTransferAdmission.TryPlan(budget, active, new[] { queued }, Fresh(1), 110, out _));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidExistingDeadlineCannotBeIgnored(double seconds)
    {
        var incumbent = new FileTransferAdmission.Work(1, 1, 258, 1024, seconds);
        Assert.False(FileTransferAdmission.TryPlan(BulkTransferBudget.Default, new[] { incumbent },
            Array.Empty<FileTransferAdmission.Work>(), Fresh(1), 120, out _));
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1L, 258L)]
    [InlineData(3L, 260L)]
    [InlineData(4L, 261L)]
    [InlineData(16384L, 20736L)]
    [InlineData(16385L, 20994L)]
    [InlineData(32771L, 41732L)]
    public void EveryFileRemainderPaysItsOwnIntegerRoundingAndFrameCost(long payload, long charge)
    {
        Assert.Equal(charge, FileTransferAdmission.AccountedFileRemainder(payload));
    }

    [Fact]
    public void LowRateNearDeadlineCannotCollapseTwoHundredFiftySixTinyFileTailsIntoOneFrame()
    {
        var budget = new BulkTransferBudget(64 * 1024, 64 * 1024, 90);
        long exact = 256L * BulkTransferBudget.Charge(1);
        Assert.Equal(66048L, exact);
        Assert.Equal(576d, BulkTransferBudget.TransferCharge(256));
        var collapsed = new FileTransferAdmission.Work(256, 256, 576, budget.ClientBytesPerSecond, 120);
        var precise = new FileTransferAdmission.Work(256, 256, exact, budget.ClientBytesPerSecond, 120, 256, 256, 100);
        Assert.True(FileTransferAdmission.TryPlan(budget, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), collapsed, 18, out _));
        Assert.False(FileTransferAdmission.TryPlan(budget, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), precise, 18, out _));
        double evenWithBurst = (exact - BulkTransferBudget.Burst(budget.ClientBytesPerSecond)) / budget.ClientBytesPerSecond;
        Assert.True(evenWithBurst + FileTransferAdmission.VerificationReserveSeconds + FileTransferAdmission.ActivationReserveSeconds > 18);
    }

    [Theory]
    [InlineData(100, 6.4, true)]
    [InlineData(1000, 64d, false)]
    public void TinyFilesRespectFourSlotRoundTripCyclesEvenWhenByteRateIsHigh(int rtt, double floor, bool fits)
    {
        var budget = BulkTransferBudget.Default;
        var work = new FileTransferAdmission.Work(256, 256, 256L * BulkTransferBudget.Charge(1),
            budget.ClientBytesPerSecond, 120, 256, 256, rtt);
        Assert.Equal(floor, work.TransactionFloorSeconds, 8);
        Assert.Equal(fits, FileTransferAdmission.TryPlan(budget, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), work, 20, out _));
    }

    [Fact]
    public void CompletionPacingProvidesAnIndependentTransactionFloorAtVeryLowRtt()
    {
        var budget = BulkTransferBudget.Default;
        var work = new FileTransferAdmission.Work(256, 256, 66048, budget.ClientBytesPerSecond, 120, 256, 256, 1);
        Assert.Equal(252d / 128, work.TransactionFloorSeconds);
        Assert.False(FileTransferAdmission.TryPlan(budget, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), work, 13, out _));
    }

    [Fact]
    public void WaitingForTransactionAndVerificationRetainsReservationWithoutSpendingUploadBandwidth()
    {
        var budget = BulkTransferBudget.Default;
        var active = Enumerable.Range(0, 8).Select(_ => new FileTransferAdmission.Work(64 * 1024 * 1024, 0, 0,
            budget.ClientBytesPerSecond, 120, 4, 0, 1000)).ToArray();
        var target = Fresh(1024 * 1024);
        Assert.True(FileTransferAdmission.TryPlan(budget, active, Array.Empty<FileTransferAdmission.Work>(), target, 16, out _));
    }

    [Fact]
    public void MaximumPlannerInventoryFitsTheBoundedDataAndCompletionEventLoop()
    {
        var active = Enumerable.Range(0, 8).Select(index =>
            new FileTransferAdmission.Work(1, 1, 258, 1000 + index, 120)).ToArray();
        var queued = Enumerable.Range(0, 32).Select(index =>
            new FileTransferAdmission.Work(1, 1, 258, 2000 + index, 120)).ToArray();
        var target = new FileTransferAdmission.Work(1, 1, 258, 3000, 120);
        Assert.True(FileTransferAdmission.TryPlan(BulkTransferBudget.Default, active, queued, target, 120, out _));
    }

    [Fact]
    public void Third_large_cold_join_can_wait_beyond_thirty_seconds_when_the_whole_plan_fits()
    {
        var active = new[] { Work(LargeFixture, LargeFixture), Work(LargeFixture, LargeFixture) };
        Assert.True(FileTransferAdmission.TryPlan(BulkTransferBudget.Default, active, Array.Empty<FileTransferAdmission.Work>(),
            Fresh(LargeFixture), 118, out double queueLimit));
        Assert.InRange(queueLimit, 50, 60);
    }

    [Fact]
    public void Capacity_that_cannot_fit_the_remaining_absolute_deadline_is_rejected_before_queuing()
    {
        var active = new[] { Work(LargeFixture, LargeFixture), Work(LargeFixture, LargeFixture) };
        Assert.False(FileTransferAdmission.TryPlan(BulkTransferBudget.Default, active, Array.Empty<FileTransferAdmission.Work>(),
            Fresh(LargeFixture), 80, out _));
        Assert.False(FileTransferAdmission.TryPlan(BulkTransferBudget.Default, active, new[] { Fresh(LargeFixture), Fresh(LargeFixture) },
            Fresh(LargeFixture), 120, out _));
        Assert.False(FileTransferAdmission.TryPlan(BulkTransferBudget.Default, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), Fresh(LargeFixture), 30, out _));
    }

    [Fact]
    public void Admission_uses_live_remaining_work_and_preserves_capacity_for_verification()
    {
        var active = new[] { Work(LargeFixture, 0), Work(LargeFixture, 0) };
        Assert.True(FileTransferAdmission.TryPlan(BulkTransferBudget.Default, active, Array.Empty<FileTransferAdmission.Work>(),
            Fresh(LargeFixture), 70, out _));
    }

    [Fact]
    public void Immediate_capacity_needs_no_positive_queue_wait_budget()
    {
        double exact = (double)FileTransferAdmission.AccountedFileRemainder(LargeFixture) / BulkTransferBudget.Default.ClientBytesPerSecond +
            FileTransferAdmission.VerificationReserveSeconds + FileTransferAdmission.ActivationReserveSeconds;
        Assert.True(FileTransferAdmission.TryPlan(BulkTransferBudget.Default, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), Fresh(LargeFixture), exact, out double queueLimit));
        Assert.Equal(0d, queueLimit);
    }

    [Fact]
    public void Upload_allowances_reserve_gameplay_and_charge_protocol_and_retransmission_headroom()
    {
        var policy = new BulkTransferBudget(8 * 1024 * 1024, 4 * 1024 * 1024, 25);
        Assert.Equal(3 * 1024 * 1024, policy.GlobalBytesPerSecond);
        Assert.Equal(policy.GlobalBytesPerSecond, policy.ClientBytesPerSecond);
        Assert.Equal(20736, BulkTransferBudget.Charge(16384));
        Assert.Equal(16384, BulkTransferBudget.PayloadAllowance(20736));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkTransferBudget(1, 4 * 1024 * 1024, 25));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkTransferBudget(8 * 1024 * 1024, 4 * 1024 * 1024, 0));
    }

    [Fact]
    public void Low_configured_upload_rate_is_respected_including_small_initial_burst()
    {
        double now = 0;
        var pump = new BulkTransferPump(() => now);
        var policy = new BulkTransferBudget(128 * 1024, 256 * 1024, 20);
        pump.Configure(policy);
        var source = new Source(); pump.Add(source);
        for (int step = 0; step <= 600; step++) { now = step / 60d; pump.Tick(); }
        Assert.True(source.Charged <= policy.ClientBytesPerSecond * 10 + BulkTransferBudget.Burst(policy.ClientBytesPerSecond));
        Assert.InRange(source.Charged, (long)(policy.ClientBytesPerSecond * 9), policy.ClientBytesPerSecond * 11);
    }

    [Fact]
    public void One_lane_at_one_hundred_ms_cannot_admit_large_files_but_sixteen_lanes_can()
    {
        var budget = BulkTransferBudget.Default;
        Assert.True(FileTransferAdmission.TryEstimateAccountedRate(budget, 1, 1024, 100, out double slow));
        Assert.True(FileTransferAdmission.TryEstimateAccountedRate(budget, 16, 1024, 100, out double fast));
        Assert.Equal(budget.ClientBytesPerSecond, fast);
        Assert.False(Plan(Fresh(128L * 1024 * 1024, slow), 120));
        Assert.False(Plan(Fresh(LargeFixture, slow), 120));
        Assert.True(Plan(Fresh(LargeFixture, fast), 120));
        Assert.True(Plan(Fresh(1024 * 1024, slow), 120));
    }

    [Fact]
    public void Unmeasured_RTT_uses_full_round_trip_fallback_and_missing_MTU_fails_cleanly()
    {
        var budget = BulkTransferBudget.Default;
        Assert.True(FileTransferAdmission.TryEstimateAccountedRate(budget, 1, 1024, 0, out double fallback));
        Assert.True(FileTransferAdmission.TryEstimateAccountedRate(budget, 1, 1024, 100, out double measured));
        Assert.Equal(measured, fallback);
        Assert.False(FileTransferAdmission.TryEstimateAccountedRate(budget, 1, 0, 100, out _));
        Assert.False(FileTransferAdmission.TryEstimateAccountedRate(budget, 0, 1024, 100, out _));
        Assert.False(FileTransferAdmission.TryEstimateAccountedRate(budget, 1, 1024, -1, out _));
    }

    [Fact]
    public void Mixed_physical_caps_share_unused_allowance_without_oversubscribing_aggregate_budget()
    {
        double[] rates = FileTransferAdmission.FairRates(new[] { 10d, 100d, 100d }, 100);
        Assert.Equal(new[] { 10d, 45d, 45d }, rates);
        Assert.Equal(100d, rates.Sum());
        var budget = new BulkTransferBudget(8 * 1024 * 1024, 4 * 1024 * 1024, 20);
        var active = new[] { Work(8 * 1024 * 1024, 8 * 1024 * 1024, 512 * 1024) };
        Assert.True(FileTransferAdmission.TryPlan(budget, active, Array.Empty<FileTransferAdmission.Work>(),
            Fresh(32L * 1024 * 1024, budget.ClientBytesPerSecond), 30, out _));
    }

    [Fact]
    public void Queued_work_keeps_its_own_physical_cap_in_the_admission_simulation()
    {
        var budget = new BulkTransferBudget(16 * 1024 * 1024, 64 * 1024 * 1024, 10);
        Assert.True(FileTransferAdmission.TryEstimateAccountedRate(budget, 1, 1024, 100, out double slow));
        // The slow incumbent must itself still fit its original deadline.
        // Retain its reservation while only its final MiB remains unsent.
        var active = new[] { Work(LargeFixture, 1024 * 1024, slow), Work(LargeFixture, LargeFixture, budget.ClientBytesPerSecond) };
        Assert.False(FileTransferAdmission.TryPlan(budget, active, new[] { Fresh(LargeFixture, slow) },
            Fresh(64L * 1024 * 1024, budget.ClientBytesPerSecond), 120, out _));
        Assert.True(FileTransferAdmission.TryPlan(budget, active, new[] { Fresh(LargeFixture, budget.ClientBytesPerSecond) },
            Fresh(64L * 1024 * 1024, budget.ClientBytesPerSecond), 120, out _));
    }

    private static bool Plan(FileTransferAdmission.Work requested, double remainingSeconds) =>
        FileTransferAdmission.TryPlan(BulkTransferBudget.Default, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), requested, remainingSeconds, out _);
    private static FileTransferAdmission.Work Work(long reserved, long remaining, double? rate = null) =>
        new FileTransferAdmission.Work(reserved, remaining, FileTransferAdmission.AccountedFileRemainder(remaining),
            rate ?? BulkTransferBudget.Default.ClientBytesPerSecond, 120);
    private static FileTransferAdmission.Work Fresh(long bytes, double? rate = null) => Work(bytes, bytes, rate);

    private sealed class Source : IBulkTransferSource
    {
        internal long Charged;
        public BulkPumpResult TrySendNext(int maximumBytes, out int sentBytes)
        {
            sentBytes = 0;
            if (maximumBytes < 16384) return BulkPumpResult.Blocked;
            sentBytes = 16384; Charged += BulkTransferBudget.Charge(sentBytes); return BulkPumpResult.Sent;
        }
    }
}
