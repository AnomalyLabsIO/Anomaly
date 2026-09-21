using Anomaly.Client.Api.Input;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Internal.Input;
using Anomaly.Client.Api.Internal.Wrappers;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class AcceptanceRuntimeTests
{
    [Fact]
    public void Input_frame_is_snapshotted_and_edge_state_precedes_reentrant_callbacks()
    {
        var dispatcher = new InputFrameDispatcher();
        var bindings = new List<InputFrameBinding>
        {
            new("example.first", default, new object()),
            new("example.second", default, new object()),
        };
        var observed = new List<string>();

        dispatcher.Update(
            bindings,
            _ => true,
            _ => true,
            binding =>
            {
                string id = binding.Id;
                observed.Add(id);
                if (id != "example.first")
                    return;

                // The nested frame sees first as already held, so it cannot
                // repeat the edge. It may publish second first, but the outer
                // frame then observes second's committed state and cannot
                // duplicate it. Mutating the source cannot suppress either.
                dispatcher.Update(
                    bindings,
                    _ => true,
                    _ => true,
                    current => observed.Add(current.Id),
                    _ => throw new InvalidOperationException("unexpected release"));
                bindings.Clear();
            },
            _ => throw new InvalidOperationException("unexpected release"));

        Assert.Equal(new[] { "example.first", "example.second" }, observed);
    }

    [Fact]
    public void Persistence_flush_is_attempted_before_scheduler_cancellation_even_on_failure()
    {
        var order = new List<string>();

        Assert.Throws<InvalidOperationException>(() => RuntimeTeardown.FlushBeforeCancel(
            () =>
            {
                order.Add("flush");
                throw new InvalidOperationException("injected flush failure");
            },
            () => order.Add("cancel")));

        Assert.Equal(new[] { "flush", "cancel" }, order);
    }

    [Fact]
    public void Serial_reuse_displaces_stale_wrapper_without_allowing_stale_eviction()
    {
        var index = new SerialOwnershipIndex<object>();
        var first = new object();
        var second = new object();

        Assert.Null(index.Assign(17, first));
        Assert.Same(first, AssertFound(index, 17));
        Assert.Same(first, index.Assign(17, second));
        Assert.False(index.RemoveIfOwned(17, first));
        Assert.Same(second, AssertFound(index, 17));
        Assert.True(index.RemoveIfOwned(17, second));
        Assert.False(index.TryGet(17, out _));
        Assert.Null(index.Assign(0, first));
        Assert.False(index.TryGet(0, out _));
    }

    [Fact]
    public void Serial_index_supports_deterministic_lifecycle_eviction()
    {
        var index = new SerialOwnershipIndex<WrapperState>();
        index.Assign(1, new WrapperState(stale: true));
        WrapperState live = new(stale: false);
        index.Assign(2, live);

        index.RemoveWhere(wrapper => wrapper.Stale);

        Assert.False(index.TryGet(1, out _));
        Assert.Same(live, AssertFound(index, 2));
        index.Clear();
        Assert.False(index.TryGet(2, out _));
    }

    private static T AssertFound<T>(SerialOwnershipIndex<T> index, ushort serial) where T : class
    {
        Assert.True(index.TryGet(serial, out T value));
        return value;
    }

    private sealed class WrapperState
    {
        internal WrapperState(bool stale) => Stale = stale;
        internal bool Stale { get; }
    }
}
