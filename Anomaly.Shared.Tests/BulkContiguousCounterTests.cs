using System;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class BulkContiguousCounterTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(2000u)]
    public void Releasing_later_frames_never_lets_a_hole_advance_beyond_the_replay_window(uint counterBase)
    {
        var bases = new uint[16]; bases[0] = counterBase;
        var sender = new BulkCreditWindow(BulkTransferProtocol.MaxWindowBytes, 128, 1, bases);
        var consumed = new BulkConsumptionTracker(bases, 1);
        var replay = new BulkReplayState();
        for (uint offset = 1; offset <= 8; offset++) sender.RecordSent(0, 1, counterBase + offset);
        for (uint offset = 2; offset <= 8; offset++)
        {
            Assert.Equal(AnomalyReplayState.RxResult.Accepted, replay.TryAcceptRx(0, counterBase + offset, out _));
            Assert.True(consumed.TryConsume(0, counterBase + offset));
        }
        var credit = new FileCreditMessage { SessionId = 1, CreditSequence = 1, ReleasedBytes = 7,
            ConsumedCounters = consumed.Snapshot() };
        credit.ReleasedFrames[0] = 7;
        Assert.True(sender.TryApplyCredit(credit, out _));
        Assert.Equal(1u, sender.OutstandingFrames);
        Assert.Equal(counterBase, sender.GetConsumedCounter(0));
        for (int retry = 0; retry < 1000; retry++) Assert.False(sender.CanSend(0, 1));
        Assert.Equal(AnomalyReplayState.RxResult.Accepted, replay.TryAcceptRx(0, counterBase + 1, out _));
        Assert.True(consumed.TryConsume(0, counterBase + 1));
        credit.CreditSequence++; credit.ReleasedBytes = 8; credit.ReleasedFrames[0] = 8;
        credit.ConsumedCounters = consumed.Snapshot();
        Assert.True(sender.TryApplyCredit(credit, out _));
        Assert.True(sender.CanSend(0, 1));
        for (uint offset = 9; offset < 200; offset++)
        {
            sender.RecordSent(0, 1, counterBase + offset);
            Assert.Equal(AnomalyReplayState.RxResult.Accepted, replay.TryAcceptRx(0, counterBase + offset, out _));
            Assert.True(consumed.TryConsume(0, counterBase + offset));
            credit.CreditSequence++; credit.ReleasedBytes++; credit.ReleasedFrames[0]++;
            credit.ConsumedCounters = consumed.Snapshot();
            Assert.True(sender.TryApplyCredit(credit, out _));
        }
        Assert.False(consumed.TryConsume(0, counterBase + 1));
    }

    [Fact]
    public void Peeked_but_backpressured_frames_do_not_spend_crypto_session_counters()
    {
        var replay = new BulkReplayState();
        for (int retry = 0; retry < 10000; retry++) Assert.Equal(1u, replay.PeekNextTxCounter(0));
        Assert.Equal(0u, replay.GetTxCounterBases()[0]);
        Assert.True(replay.TryCommitTxCounter(0, 1));
        Assert.False(replay.TryCommitTxCounter(0, 1));
        Assert.False(replay.TryCommitTxCounter(0, 3));
        Assert.Equal(2u, replay.PeekNextTxCounter(0));
        Assert.True(replay.TryCommitTxCounter(0, 2));
        Assert.Equal(2u, replay.GetTxCounterBases()[0]);
        Assert.Equal(1u, replay.PeekNextTxCounter(1));
    }

    [Fact]
    public void Consumed_watermarks_are_bounded_atomic_and_independent_of_released_counts()
    {
        var window = new BulkCreditWindow(BulkTransferProtocol.MaxWindowBytes, 128, 1);
        window.RecordSent(0, 1);
        var credit = new FileCreditMessage { SessionId = 1, CreditSequence = 1, ReleasedBytes = 1 };
        credit.ReleasedFrames[0] = 1; credit.ConsumedCounters[0] = 2;
        Assert.False(window.TryApplyCredit(credit, out _));
        Assert.Equal(0UL, window.ReleasedBytes);
        credit.ConsumedCounters[0] = 1;
        Assert.True(window.TryApplyCredit(credit, out _));
        credit.ConsumedCounters[0] = 0;
        Assert.False(window.TryApplyCredit(credit, out _));
        var tracker = new BulkConsumptionTracker(null, 1);
        Assert.False(tracker.TryConsume(0, 9));
        Assert.False(tracker.TryConsume(1, 1));
        Assert.True(tracker.TryConsume(0, 8));
        Assert.False(tracker.TryConsume(0, 8));
        Assert.Equal(0u, tracker.Snapshot()[0]);
    }
}
