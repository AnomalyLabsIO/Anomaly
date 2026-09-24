using Anomaly.Server.Assets;
using Anomaly.Shared.Assets;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class DecorativeVisualStateTests
{
    private static readonly AssetAddress Address = AssetAddress.Parse("mods/fixture/visuals/quad");
    private static DecorativeVisualState.Entry Spawn(DecorativeVisualState state, long owner = 1, int triangles = 2) =>
        state.Spawn(owner, Address, new byte[32], triangles, new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f, 1f }, new[] { 1f, 1f, 1f });

    [Fact]
    public void Limits_aggregate_instances_and_triangles_without_mutating_on_rejection()
    {
        var state = new DecorativeVisualState();
        for (int i = 0; i < 128; i++) Spawn(state);
        ulong revision = state.Revision;
        Assert.Throws<InvalidOperationException>(() => Spawn(state));
        Assert.Equal(revision, state.Revision);
        state.Clear();
        for (int i = 0; i < 10; i++) Spawn(state, triangles: 100000);
        Assert.Throws<InvalidOperationException>(() => Spawn(state, triangles: 1));
        Assert.Equal(1000000, state.Triangles);
    }
    [Fact]
    public void Scope_ownership_and_round_generation_prevent_stale_handle_reuse()
    {
        var state = new DecorativeVisualState();
        var first = Spawn(state, owner: 1);
        Assert.Throws<InvalidOperationException>(() => state.Remove(2, first.Instance.Id));
        Assert.Single(state.OwnedIds(1));
        ulong generation = state.Generation;
        state.Clear();
        Assert.True(state.Generation > generation);
        Assert.False(state.Remove(1, first.Instance.Id));
        var second = Spawn(state, owner: 1);
        Assert.NotEqual(first.Instance.Id, second.Instance.Id);
    }
    [Fact]
    public void Invalid_transform_does_not_modify_existing_state_and_arrays_are_copied()
    {
        var state = new DecorativeVisualState();
        var first = Spawn(state);
        var position = new[] { 4f, 5f, 6f };
        state.Update(1, first.Instance.Id, position, new[] { 0f, 0f, 0f, 1f }, new[] { 1f, 1f, 1f });
        position[0] = 99;
        Assert.Equal(4f, first.Instance.Position[0]);
        ulong revision = state.Revision;
        Assert.ThrowsAny<Exception>(() => state.Update(1, first.Instance.Id, new[] { float.NaN, 0f, 0f }, new[] { 0f, 0f, 0f, 1f }, new[] { 1f, 1f, 1f }));
        Assert.Equal(revision, state.Revision);
        Assert.Equal(4f, first.Instance.Position[0]);
    }
}
