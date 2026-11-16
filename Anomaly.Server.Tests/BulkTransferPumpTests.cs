using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class BulkTransferPumpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledSourcesAreUnlinkedEvenWithoutAnotherSuccessfulSend(bool keepBlockedOwner)
    {
        var pump = new BulkTransferPump(() => 0);
        var blocked = new Source { Blocked = true };
        if (keepBlockedOwner) pump.Add(blocked);
        int expected = keepBlockedOwner ? 1 : 0;
        for (int index = 0; index < 1000; index++)
        {
            var cancelled = new Source();
            pump.Add(cancelled);
            pump.Remove(cancelled);
            Assert.Equal(expected, pump.Count);
            Assert.Equal(expected, pump.QueuedCount);
            pump.Tick();
            Assert.Equal(0, cancelled.Frames);
            Assert.Equal(expected, pump.QueuedCount);
        }
        pump.Remove(blocked);
        Assert.Equal(0, pump.QueuedCount);
    }

    [Fact]
    public void CancellationDuringSendUnlinksOtherOwnersAndDoesNotRequeueTheCancelledOwner()
    {
        var order = new List<int>();
        var pump = new BulkTransferPump(() => 0);
        var first = new Source(1, order);
        var cancelled = new Source(2, order);
        var last = new Source(3, order);
        first.AfterSend = () => { pump.Remove(first); pump.Remove(cancelled); };
        pump.Add(first); pump.Add(cancelled); pump.Add(last);
        pump.Tick();
        Assert.Equal(1, first.Frames);
        Assert.Equal(0, cancelled.Frames);
        Assert.Equal(1, pump.Count);
        Assert.Equal(1, pump.QueuedCount);
        Assert.Equal(3, order[1]);
        pump.Remove(last);
        Assert.Equal(0, pump.QueuedCount);
    }

    [Fact]
    public void ReentrantReregistrationHasOnlyOneLiveQueueNode()
    {
        var pump = new BulkTransferPump(() => 0);
        var source = new Source();
        source.AfterSend = () =>
        {
            source.AfterSend = null;
            pump.Remove(source);
            pump.Add(source);
        };
        pump.Add(source); pump.Tick();
        Assert.Equal(1, pump.Count);
        Assert.Equal(1, pump.QueuedCount);
        pump.Remove(source);
        Assert.Equal(0, pump.QueuedCount);
    }

    [Fact]
    public void One_update_sends_multiple_frames_and_round_robins_all_eight_clients()
    {
        double now = 0;
        var order = new List<int>();
        var pump = new BulkTransferPump(() => now);
        var clients = Enumerable.Range(0, 8).Select(id => new Source(id, order)).ToArray();
        foreach (var client in clients) pump.Add(client);

        pump.Tick();

        Assert.Equal(25, order.Count);
        Assert.Equal(Enumerable.Range(0, 8), order.Take(8));
        Assert.All(clients, client => Assert.InRange(client.Frames, 3, 4));
        Assert.True(clients.Sum(client => client.ChargedBytes) <= 512 * 1024);
    }

    [Fact]
    public void Empty_rate_buckets_cannot_be_refilled_by_repeated_updates_without_elapsed_time()
    {
        double now = 0;
        var pump = new BulkTransferPump(() => now);
        var client = new Source(); pump.Add(client);
        pump.Tick();
        int first = client.Bytes;
        for (int i = 0; i < 100; i++) pump.Tick();
        Assert.Equal(25 * FileChunkMessage.MaxChunkBytes, first);
        Assert.Equal(first, client.Bytes);
        now = 1d / 60;
        pump.Tick();
        Assert.InRange(client.Bytes - first, 5 * FileChunkMessage.MaxChunkBytes, 6 * FileChunkMessage.MaxChunkBytes);
    }

    [Fact]
    public void Steady_60Hz_updates_respect_global_and_client_byte_rates_including_the_bounded_initial_burst()
    {
        double now = 0;
        var pump = new BulkTransferPump(() => now);
        var clients = Enumerable.Range(0, 8).Select(_ => new Source()).ToArray();
        foreach (var client in clients) pump.Add(client);
        for (int frame = 0; frame <= 60; frame++) { now = frame / 60d; pump.Tick(); }
        Assert.InRange(clients.Sum(client => (long)client.ChargedBytes), 12L * 1024 * 1024,
            pump.Budget.GlobalBytesPerSecond + BulkTransferPump.MaximumBytesPerUpdate);
        Assert.All(clients, client => Assert.True(client.ChargedBytes <=
            pump.Budget.ClientBytesPerSecond + BulkTransferPump.MaximumBytesPerUpdate));
        Assert.True(clients.Max(client => client.Frames) - clients.Min(client => client.Frames) <= 1);
    }

    [Fact]
    public void Backpressured_client_retains_its_turn_without_starving_other_clients()
    {
        var pump = new BulkTransferPump(() => 0);
        var blocked = new Source { Blocked = true };
        var ready = new Source(); pump.Add(blocked); pump.Add(ready);
        pump.Tick();
        Assert.Equal(0, blocked.Bytes);
        Assert.Equal(25, ready.Frames);
        blocked.Blocked = false;
        pump.Remove(ready); pump.Reset(); pump.Add(blocked);
        pump.Tick();
        Assert.Equal(25, blocked.Frames);
    }

    [Fact]
    public void Small_frames_obey_the_frame_cap_and_measured_work_obeys_the_time_cap()
    {
        double now = 0;
        var pump = new BulkTransferPump(() => now);
        var small = new Source { FrameBytes = 16 };
        pump.Add(small); pump.Tick();
        Assert.Equal(32, small.Frames);
        Assert.Equal(512, small.Bytes);
        pump.Reset();
        var slow = new Source { AfterSend = () => now += .001 };
        pump.Add(slow); pump.Tick();
        Assert.Equal(2, slow.Frames);
    }

    [Fact]
    public void Removed_and_finished_sources_cannot_send_after_cancellation_or_re_registration()
    {
        var pump = new BulkTransferPump(() => 0);
        var removed = new Source(); var finished = new Source { Finished = true }; var active = new Source();
        pump.Add(removed); pump.Remove(removed); pump.Add(finished); pump.Add(active);
        pump.Tick();
        Assert.Equal(0, removed.Frames); Assert.Equal(0, finished.Frames);
        Assert.Equal(25, active.Frames); Assert.Equal(1, pump.Count);
        pump.Reset(); pump.Add(removed); pump.Remove(removed); pump.Add(removed);
        pump.Tick();
        Assert.Equal(25, removed.Frames);
    }

    private sealed class Source : IBulkTransferSource
    {
        private readonly int _id;
        private readonly List<int> _order;
        internal int FrameBytes = FileChunkMessage.MaxChunkBytes;
        internal int Frames, Bytes, ChargedBytes;
        internal bool Blocked, Finished;
        internal Action AfterSend;
        internal Source(int id = 0, List<int> order = null) { _id = id; _order = order; }
        public BulkPumpResult TrySendNext(int maximumBytes, out int sentBytes)
        {
            sentBytes = 0;
            if (Finished) return BulkPumpResult.Finished;
            if (Blocked || maximumBytes < FrameBytes) return BulkPumpResult.Blocked;
            sentBytes = FrameBytes; Frames++; Bytes += sentBytes; ChargedBytes += BulkTransferBudget.Charge(sentBytes);
            _order?.Add(_id); AfterSend?.Invoke();
            return BulkPumpResult.Sent;
        }
    }
}
