using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class BulkSchedulerCapacityTests
{
    [Fact]
    public void StartupIsExplicitlyUnmeasuredAndNeverPromisesMoreThanOneDefaultClientAllowance()
    {
        var pump = new BulkTransferPump(() => 0);
        var estimate = pump.AdmissionCapacity;
        Assert.False(estimate.IsMeasured);
        Assert.Equal("unmeasured-service-cost", estimate.Reason);
        Assert.Equal(BulkTransferBudget.Default.ClientBytesPerSecond, estimate.AccountedBytesPerSecond);
        pump.Configure(new BulkTransferBudget(16 * 1024 * 1024, 64 * 1024 * 1024, 10));
        Assert.Equal(BulkTransferBudget.Default.ClientBytesPerSecond, pump.AdmissionCapacity.AccountedBytesPerSecond);
        pump.Configure(new BulkTransferBudget(128 * 1024, 64 * 1024, 20));
        Assert.Equal(pump.Budget.GlobalBytesPerSecond, pump.AdmissionCapacity.AccountedBytesPerSecond);
    }

    [Fact]
    public void TimeLimitedReadyPumpRejectsTheThirdLargeRequestThatConfiguredOnlyAdmissionOverpromises()
    {
        double now = 0;
        var pump = new BulkTransferPump(() => now);
        var first = new Source(seconds => now += seconds) { SendSeconds = .00028 };
        var second = new Source(seconds => now += seconds) { SendSeconds = .00028 };
        pump.Add(first); pump.Add(second);
        Train(pump, () => now, value => now = value, 240, 60);
        var estimate = pump.AdmissionCapacity;
        Assert.True(estimate.IsMeasured);
        Assert.Equal("measured-ready-time-limit", estimate.Reason);
        Assert.InRange(estimate.AccountedBytesPerSecond, 7 * 1024 * 1024, 10 * 1024 * 1024);
        Assert.True(estimate.AccountedBytesPerSecond < pump.Budget.GlobalBytesPerSecond);
        Assert.InRange(estimate.ServiceSamples, 32, 128);
        Assert.InRange(estimate.CadenceSamples, 8, 64);
        Assert.True(estimate.SaturatedTicks >= 4);

        const long fullRequest = 267696453;
        var active = new[] { Work(fullRequest, 220000000, pump), Work(fullRequest, 220000000, pump) };
        var requested = Work(fullRequest, fullRequest, pump);
        Assert.True(FileTransferAdmission.TryPlan(pump.Budget, active, Array.Empty<FileTransferAdmission.Work>(),
            requested, 120, out double oldWait));
        Assert.InRange(oldWait, 30, 60);
        Assert.False(FileTransferAdmission.TryPlan(pump.Budget, active, Array.Empty<FileTransferAdmission.Work>(),
            requested, 120, out _, estimate.AccountedBytesPerSecond));
        Assert.Equal(.002, BulkTransferPump.MaximumSecondsPerUpdate);
        Assert.Equal(32, BulkTransferPump.MaximumFramesPerUpdate);
        Assert.Equal(512 * 1024, BulkTransferPump.MaximumBytesPerUpdate);
    }

    [Fact]
    public void NetworkCreditOrStorageBlockingDoesNotTeachAZeroGoodputCpuCapacity()
    {
        double now = 0;
        var pump = new BulkTransferPump(() => now);
        var source = new Source(seconds => now += seconds) { SendSeconds = .0005 };
        pump.Add(source);
        Train(pump, () => now, value => now = value, 240, 60);
        var ready = pump.AdmissionCapacity;
        source.Blocked = true;
        Train(pump, () => now, value => now = value, 240, 60);
        var blocked = pump.AdmissionCapacity;
        Assert.True(blocked.IsMeasured);
        Assert.Equal(ready.ServiceSamples, blocked.ServiceSamples);
        Assert.Equal(ready.SuccessfulSendMicroseconds, blocked.SuccessfulSendMicroseconds, 5);
        Assert.InRange(blocked.AccountedBytesPerSecond / ready.AccountedBytesPerSecond, .99, 1.01);
        Assert.Equal(0, blocked.SaturatedTicks);
    }

    [Fact]
    public void ExpensiveBlockedPollingReservesRealCpuInsteadOfIgnoringItsCost()
    {
        double now = 0;
        var pump = new BulkTransferPump(() => now);
        var ready = new Source(seconds => now += seconds) { SendSeconds = .0002 };
        pump.Add(ready);
        Train(pump, () => now, value => now = value, 240, 60);
        double before = pump.AdmissionCapacity.AccountedBytesPerSecond;
        pump.Add(new Source(seconds => now += seconds) { Blocked = true, BlockedSeconds = .001 });
        Train(pump, () => now, value => now = value, 240, 60);
        var estimate = pump.AdmissionCapacity;
        Assert.True(estimate.IsMeasured);
        Assert.InRange(estimate.OtherMillisecondsPerTick, 1, 2.1);
        Assert.True(estimate.AccountedBytesPerSecond < before / 2);
        Assert.Equal(0, estimate.SaturatedTicks);
    }

    [Fact]
    public void ActualUpdateCadenceConstrainsTheSameReadySendServiceCost()
    {
        double now = 0;
        var pump = new BulkTransferPump(() => now);
        pump.Add(new Source(seconds => now += seconds) { SendSeconds = .0005 });
        Train(pump, () => now, value => now = value, 240, 60);
        var fast = pump.AdmissionCapacity;
        Train(pump, () => now, value => now = value, 240, 20);
        var slow = pump.AdmissionCapacity;
        Assert.Equal(fast.SuccessfulSendMicroseconds, slow.SuccessfulSendMicroseconds, 5);
        Assert.InRange(slow.CadenceMilliseconds, 49.9, 50.1);
        Assert.InRange(slow.AccountedBytesPerSecond / fast.AccountedBytesPerSecond, .32, .35);
    }

    [Fact]
    public void InactiveTimeRequiresFreshCadenceInsteadOfBecomingAOneHourActiveFrame()
    {
        double now = 0;
        var pump = new BulkTransferPump(() => now);
        var source = new Source(seconds => now += seconds) { SendSeconds = .0005 };
        pump.Add(source);
        Train(pump, () => now, value => now = value, 240, 60);
        pump.Remove(source);
        now += 3600;
        pump.Tick();
        pump.Add(source);
        pump.Tick();
        Assert.False(pump.AdmissionCapacity.IsMeasured);
        Assert.Equal("unmeasured-update-cadence", pump.AdmissionCapacity.Reason);
        Assert.Equal(BulkTransferBudget.Default.ClientBytesPerSecond, pump.AdmissionCapacity.AccountedBytesPerSecond);
    }

    [Fact]
    public void RetiringAnExhaustedPollingWorkloadAllowsFreshAdmissionAndRequalification()
    {
        double now = 0;
        var pump = new BulkTransferPump(() => now);
        var source = new Source(seconds => now += seconds) { SendSeconds = .0005 };
        pump.Add(source);
        Train(pump, () => now, value => now = value, 240, 60);
        source.Blocked = true; source.BlockedSeconds = .003;
        Train(pump, () => now, value => now = value, 128, 60);
        Assert.True(pump.AdmissionCapacity.IsMeasured);
        Assert.Equal(0, pump.AdmissionCapacity.AccountedBytesPerSecond);
        var work = Work(267696453, 267696453, pump);
        Assert.False(FileTransferAdmission.TryPlan(pump.Budget, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), work, 120, out _, pump.AdmissionCapacity.AccountedBytesPerSecond));

        pump.Remove(source);
        Assert.False(pump.AdmissionCapacity.IsMeasured);
        Assert.True(FileTransferAdmission.TryPlan(pump.Budget, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), work, 120, out _, pump.AdmissionCapacity.AccountedBytesPerSecond));
        now += 60;
        pump.Add(new Source(seconds => now += seconds) { SendSeconds = .0003 });
        Train(pump, () => now, value => now = value, 240, 60);
        Assert.True(pump.AdmissionCapacity.IsMeasured);
        Assert.True(pump.AdmissionCapacity.AccountedBytesPerSecond > 0);
        Assert.InRange(pump.AdmissionCapacity.OtherMillisecondsPerTick, 0, .001);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidOrExhaustedSchedulerCapacityCannotAnnounceAQueuePromise(double capacity)
    {
        var work = new FileTransferAdmission.Work(1, 1, FileTransferAdmission.AccountedFileRemainder(1), 1024 * 1024, 120);
        Assert.False(FileTransferAdmission.TryPlan(BulkTransferBudget.Default, Array.Empty<FileTransferAdmission.Work>(),
            Array.Empty<FileTransferAdmission.Work>(), work, 120, out _, capacity));
    }

    private static FileTransferAdmission.Work Work(long reserved, long remaining, BulkTransferPump pump) =>
        new FileTransferAdmission.Work(reserved, remaining, FileTransferAdmission.AccountedFileRemainder(remaining), pump.Budget.ClientBytesPerSecond, 120);

    private static void Train(BulkTransferPump pump, Func<double> getTime, Action<double> setTime, int frames, double frequency)
    {
        double next = getTime();
        for (int frame = 0; frame < frames; frame++)
        {
            setTime(next); pump.Tick(); next += 1 / frequency;
        }
        setTime(next);
    }

    private sealed class Source : IBulkTransferSource
    {
        private readonly Action<double> _spend;
        internal double SendSeconds, BlockedSeconds;
        internal bool Blocked;
        internal Source(Action<double> spend) => _spend = spend;
        public BulkPumpResult TrySendNext(int maximumBytes, out int sentBytes)
        {
            sentBytes = 0;
            if (Blocked || maximumBytes < FileChunkMessage.MaxChunkBytes)
            { _spend(BlockedSeconds); return BulkPumpResult.Blocked; }
            _spend(SendSeconds);
            sentBytes = FileChunkMessage.MaxChunkBytes;
            return BulkPumpResult.Sent;
        }
    }
}
