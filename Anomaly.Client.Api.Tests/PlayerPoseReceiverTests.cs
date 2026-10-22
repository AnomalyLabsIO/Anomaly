using Anomaly.Client.Api.Internal.Poses;
using Anomaly.Shared.Poses;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class PlayerPoseReceiverTests
{
    [Fact]
    public void Samples_cannot_create_a_stream_or_resurrect_clear()
    {
        var receiver = new PoseReceiver(); receiver.Reset(1);
        Assert.False(receiver.Apply(Chunk(2, 1.05), 10.05));
        Assert.True(receiver.Bind(Binding())); Assert.True(receiver.Apply(Activate(), 10));
        Assert.True(receiver.Apply(Chunk(2, 1.05), 10.05));
        Assert.True(receiver.Apply(new PoseControl { Operation = PoseControlOperation.Clear, Binding = Binding(), OverrideGeneration = 1 }, 10.1));
        Assert.False(receiver.Apply(Chunk(3, 1.1), 10.1));
        Assert.False(receiver.Apply(Activate(), 10.1));
        Assert.False(receiver.TryGet(1, 10.1, out _, out _));
    }

    [Fact]
    public void Model_replacement_and_round_reset_discard_old_samples()
    {
        var receiver = Started();
        var next = Binding(); next.ModelGeneration++; next.BindingRevision++;
        Assert.True(receiver.Bind(next));
        Assert.False(receiver.Apply(Chunk(2, 1.05), 10.05));
        Assert.False(receiver.Apply(Activate(), 10.05));
        receiver.Reset(2); Assert.False(receiver.Apply(Chunk(2, 1.05), 10.05));
    }

    [Fact]
    public void Frames_are_atomic_reordered_bounded_and_expire_after_250ms()
    {
        var receiver = Started(twoBones: true);
        var second = Chunk(2, 1.05, id: 6); second.ChunkIndex = 1; second.ChunkCount = 2;
        var first = Chunk(2, 1.05); first.ChunkCount = 2;
        Assert.False(receiver.Apply(second, 10.05)); Assert.Equal(1, receiver.PendingAssemblyCount);
        Assert.True(receiver.Apply(first, 10.1)); Assert.Equal(0, receiver.PendingAssemblyCount);
        Assert.False(receiver.Apply(first, 10.11));
        var incomplete = Chunk(3, 1.2); incomplete.ChunkCount = 2;
        Assert.False(receiver.Apply(incomplete, 10.2));
        receiver.TryGet(1, 10.46, out _, out _); Assert.Equal(0, receiver.PendingAssemblyCount);
    }

    [Fact]
    public void Interpolates_on_server_time_without_extrapolation_and_fades_stalls()
    {
        var receiver = Started();
        var chunk = Chunk(2, 1.2, x: 2);
        Assert.True(receiver.Apply(chunk, 10.2));
        Assert.True(receiver.TryGet(1, 10.2, out var halfway, out _));
        Assert.InRange(halfway.Bones[0].X, 0.9999f, 1.0001f);
        Assert.True(receiver.TryGet(1, 10.4, out var held, out _)); Assert.Equal(2, held.Bones[0].X);
        Assert.True(receiver.TryGet(1, 10.775, out var faded, out _));
        Assert.InRange(faded.Bones[0].Weight, 0.499f, 0.501f);
        Assert.False(receiver.TryGet(1, 10.851, out _, out _));
    }

    [Fact]
    public void Shape_changes_require_reliable_new_generation()
    {
        var receiver = Started();
        Assert.False(receiver.Apply(Chunk(2, 1.1, id: 6), 10.1));
        var next = Activate(); next.OverrideGeneration = 2; next.Frame = Frame(6);
        Assert.True(receiver.Apply(next, 10.1));
        Assert.False(receiver.Apply(Chunk(3, 1.2), 10.2));
    }

    [Fact]
    public void Same_role_invalidation_keeps_tombstone_until_new_generation()
    {
        var receiver = Started(); receiver.Invalidate(1);
        Assert.True(receiver.Bind(Binding()));
        Assert.False(receiver.Apply(Activate(), 10.1));
        Assert.False(receiver.Apply(Chunk(2, 1.1), 10.1));
    }

    [Fact]
    public void Destroyed_subjects_release_capacity_without_accepting_delayed_traffic()
    {
        var receiver = new PoseReceiver(); receiver.Reset(1);
        for (uint id = 1; id <= 1024; id++)
        {
            var binding = Binding(); binding.NetId = id;
            Assert.True(receiver.Bind(binding));
            var activation = Activate(); activation.Binding = binding;
            Assert.True(receiver.Apply(activation, 10));
            receiver.Forget(id);
            Assert.False(receiver.Apply(activation, 10.1));
            var chunk = Chunk(2, 1.1); chunk.NetId = id;
            Assert.False(receiver.Apply(chunk, 10.1));
        }
        var replacement = Binding(); replacement.ModelGeneration++; replacement.BindingRevision++;
        Assert.True(receiver.Bind(replacement));
        Assert.False(receiver.Apply(Activate(), 10.2));
        Assert.False(receiver.Apply(Chunk(3, 1.2), 10.2));
    }

    private static PoseReceiver Started(bool twoBones = false)
    {
        var receiver = new PoseReceiver(); receiver.Reset(1); receiver.Bind(Binding());
        var activation = Activate(); if (twoBones) activation.Frame = new PoseFrame("anomaly.guard", 1, new[] { Bone(5), Bone(6) });
        Assert.True(receiver.Apply(activation, 10)); return receiver;
    }
    private static PoseBinding Binding() => new() { NetId = 1, ModelGeneration = 1, BindingRevision = 1, RoleId = 15,
        RigId = "anomaly.guard", SchemaVersion = 1 };
    private static PoseFrame Frame(ushort id = 5) => new("anomaly.guard", 1, new[] { Bone(id) });
    private static PoseBone Bone(ushort id, float x = 0) => new(id, PoseComponents.Position | PoseComponents.Rotation, x, 0, 0, 0, 0, 0, 1);
    private static PoseControl Activate() => new() { Operation = PoseControlOperation.Activate, Binding = Binding(),
        OverrideGeneration = 1, Sequence = 1, ServerTime = 1, Frame = Frame() };
    private static PoseSampleChunk Chunk(ulong sequence, double time, ushort id = 5, float x = 1)
    {
        var chunk = new PoseSampleChunk { NetId = 1, ModelGeneration = 1, BindingRevision = 1, OverrideGeneration = 1,
            Sequence = sequence, ServerTime = time, ChunkCount = 1 };
        chunk.Bones.Add(Bone(id, x)); return chunk;
    }
}
