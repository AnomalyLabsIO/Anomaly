using Anomaly.Client.Api.Internal;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class EntityLifecyclePublicationGateTests
{
    [Fact]
    public void First_added_publication_owns_its_transition_until_removal()
    {
        var gate = new EntityLifecyclePublicationGate();
        Assert.True(gate.TryAdd(17, out long added));
        Assert.False(gate.TryAdd(17, out _));
        Assert.True(gate.IsCurrent(17, added));
    }

    [Fact]
    public void Repeated_removal_is_rejected_before_it_can_recreate_a_wrapper()
    {
        var gate = new EntityLifecyclePublicationGate();
        int wrappersCreated = 0;
        int removedEvents = 0;

        void Remove()
        {
            if (!gate.TryRemove(17, out _)) return;
            wrappersCreated++;
            removedEvents++;
            Remove(); // A subscriber reenters before the native destroy call returns.
        }

        Remove();
        Remove(); // A later callback is still the same lifetime transition.
        Assert.Equal(1, wrappersCreated);
        Assert.Equal(1, removedEvents);
    }

    [Fact]
    public void Same_instance_can_be_added_removed_and_added_without_a_frame_delay()
    {
        var gate = new EntityLifecyclePublicationGate();
        Assert.True(gate.TryAdd(17, out long firstAdd));
        Assert.True(gate.TryRemove(17, out long removal));
        Assert.True(gate.TryAdd(17, out long secondAdd));

        Assert.False(gate.IsCurrent(17, firstAdd));
        Assert.False(gate.IsCurrent(17, removal));
        Assert.True(gate.IsCurrent(17, secondAdd));
        Assert.True(gate.TryRemove(17, out _));
    }

    [Fact]
    public void Scene_or_connection_reset_invalidates_in_progress_notifications()
    {
        var gate = new EntityLifecyclePublicationGate();
        Assert.True(gate.TryRemove(17, out long previousRemoval));
        gate.Reset();
        Assert.False(gate.IsCurrent(17, previousRemoval));
        Assert.True(gate.TryAdd(17, out long nextAdded));
        Assert.NotEqual(previousRemoval, nextAdded);
        Assert.False(gate.IsCurrent(17, previousRemoval));
        Assert.True(gate.IsCurrent(17, nextAdded));
    }

    [Fact]
    public void Different_entities_and_invalid_identity_do_not_share_publications()
    {
        var gate = new EntityLifecyclePublicationGate();
        Assert.False(gate.TryAdd(0, out _));
        Assert.False(gate.TryRemove(0, out _));
        Assert.True(gate.TryAdd(17, out _));
        Assert.True(gate.TryAdd(18, out _));
        Assert.True(gate.TryRemove(17, out _));
        Assert.False(gate.TryAdd(18, out _));
    }
}
