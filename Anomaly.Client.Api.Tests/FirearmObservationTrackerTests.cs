using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Items;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public class FirearmObservationTrackerTests
{
    [Fact]
    public void ReceivedEmptyMagazineIsAChangeFromUnavailableState()
    {
        var tracker = new FirearmObservationTracker<object>();
        var item = new object();
        tracker.TryObserve(item, default, out _, out long unknown);
        var empty = new FirearmState(0, 30, false, null, null, null);

        Assert.True(tracker.TryObserve(item, empty, out FirearmState? previous, out long received));
        Assert.True(previous.HasValue);
        Assert.Null(previous.Value.MagazineAmmo);
        Assert.False(tracker.IsCurrent(item, unknown));
        Assert.True(tracker.IsCurrent(item, received));
        Assert.False(tracker.TryObserve(item, empty, out _, out _));
    }

    [Fact]
    public void ReconnectDuringCallbackInvalidatesOldBatchEvenWithIdenticalState()
    {
        var tracker = new FirearmObservationTracker<object>();
        var item = new object();
        var state = new FirearmState(12, 30, true, false, false, false);
        tracker.TryObserve(item, state, out _, out long oldBatch);

        tracker.Clear();
        Assert.True(tracker.TryObserve(item, state, out FirearmState? previous, out long newBatch));

        Assert.Null(previous);
        Assert.False(tracker.IsCurrent(item, oldBatch));
        Assert.True(tracker.IsCurrent(item, newBatch));
    }

    [Fact]
    public void NestedChangeAndReturnToOriginalValuesStillInvalidatesOldBatch()
    {
        var tracker = new FirearmObservationTracker<object>();
        var item = new object();
        var state = new FirearmState(12, 30, true, false, false, false);
        tracker.TryObserve(item, state, out _, out long original);
        tracker.TryObserve(item, new FirearmState(11, 30, true, true, false, false), out _, out _);
        tracker.TryObserve(item, state, out _, out long latest);

        Assert.False(tracker.IsCurrent(item, original));
        Assert.True(tracker.IsCurrent(item, latest));
    }

    [Fact]
    public void RemovingItemInvalidatesItsBatchWithoutInvalidatingOtherItems()
    {
        var tracker = new FirearmObservationTracker<object>();
        var first = new object();
        var second = new object();
        tracker.TryObserve(first, default, out _, out long firstBatch);
        tracker.TryObserve(second, default, out _, out long secondBatch);

        tracker.Remove(first);

        Assert.False(tracker.IsCurrent(first, firstBatch));
        Assert.True(tracker.IsCurrent(second, secondBatch));
    }
}
