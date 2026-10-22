using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Internal;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public class ClientObservationContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VetoSurvivesLaterSubscribersAndThrowingHandlers(bool throwAfterVeto)
    {
        var args = new CancellableArgs();
        var observed = new List<bool>();
        AnomalyEventHandler<CancellableArgs> handlers = e =>
        {
            e.IsAllowed = false;
            if (throwAfterVeto)
                throw new InvalidOperationException("subscriber failed after cancelling");
        };
        handlers += e => { observed.Add(e.IsAllowed); e.IsAllowed = true; };
        handlers += e => observed.Add(e.IsAllowed);

        EventInvoker.Invoke(handlers, args);

        Assert.Equal(new[] { false, false }, observed);
        Assert.False(args.IsAllowed);
    }

    [Fact]
    public void InitiallyDeniedEventCannotBeReallowed()
    {
        var args = new CancellableArgs { IsAllowed = false };
        EventInvoker.Invoke<CancellableArgs>(e => e.IsAllowed = true, args);
        Assert.False(args.IsAllowed);
    }

    [Theory]
    [InlineData(false, true, false, 2, false)]
    [InlineData(true, true, true, 0, true)]
    [InlineData(true, false, false, 0, false)]
    [InlineData(true, false, true, 1, false)]
    [InlineData(true, false, false, 1, true)]
    [InlineData(true, true, true, 1, true)]
    [InlineData(true, false, true, 2, true)]
    [InlineData(true, true, false, 99, false)]
    public void StatsRespectOwnerAndObserverReplicationScope(
        bool hasViewer, bool isOwner, bool viewerAlive, int syncMode, bool expected)
    {
        Assert.Equal(expected, StatVisibility.CanObserve(hasViewer, isOwner, viewerAlive, syncMode));
    }

    private sealed class CancellableArgs : EventArgs, ICancellableEvent
    {
        public bool IsAllowed { get; set; } = true;
    }

    [Fact]
    public void StatsRebaseAcrossVisibilityAndRoleChangesWithoutFalseHealing()
    {
        var state = new StatObservation();
        Assert.False(state.Sample(1, 10, true, 50, 100, out _, out _));
        Assert.True(state.Sample(1, 10, true, 40, 100, out float previous, out _));
        Assert.Equal(50, previous);
        Assert.False(state.Sample(1, 10, false, 0, 100, out _, out _));
        Assert.False(state.Sample(1, 10, true, 90, 100, out _, out _));
        Assert.False(state.Sample(2, 10, true, 100, 150, out _, out _));
        Assert.False(state.Sample(2, 11, true, 120, 150, out _, out _));
        Assert.True(state.Sample(2, 11, true, 120, 200, out _, out float previousMaximum));
        Assert.Equal(150, previousMaximum);
        Assert.False(state.Sample(2, 11, true, float.NaN, 200, out _, out _));
        Assert.False(state.Sample(2, 11, true, 150, 200, out _, out _));
    }

    [Fact]
    public void EnrollmentNotificationsFollowDisclosureAndClearWithoutRepeatingIdenticalValues()
    {
        Features.Client.EnrollmentId = null;
        var changes = new List<(string?, string?)>();
        AnomalyEventHandler<Events.Args.EnrollmentChangedEventArgs> handler = args =>
            changes.Add((args.Previous, args.Current));
        Events.Handlers.ClientEvents.EnrollmentChanged += handler;
        try
        {
            Features.Client.EnrollmentId = "test-enrollment";
            Features.Client.EnrollmentId = "test-enrollment";
            Features.Client.EnrollmentId = null;
            Assert.Equal(new[] { ((string?)null, (string?)"test-enrollment"), ("test-enrollment", (string?)null) }, changes);
        }
        finally
        {
            Events.Handlers.ClientEvents.EnrollmentChanged -= handler;
            Features.Client.EnrollmentId = null;
        }
    }
}
