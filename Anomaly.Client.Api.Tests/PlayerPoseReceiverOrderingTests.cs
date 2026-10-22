using Anomaly.Client.Api.Internal.Poses;
using Anomaly.Shared.Poses;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class PlayerPoseReceiverOrderingTests
{
    [Fact]
    public void Older_clear_does_not_remove_a_replacement_but_newer_clear_is_a_tombstone()
    {
        var receiver = Start();
        Assert.True(receiver.Apply(Activation(2, 2, 1.1), 10.1));
        Assert.False(receiver.Apply(Clear(1), 10.11));
        Assert.True(receiver.TryGet(1, 10.11, out _, out _));
        Assert.True(receiver.Apply(Clear(3), 10.12));
        Assert.False(receiver.Apply(Activation(2, 3, 1.2), 10.2));
        Assert.False(receiver.Apply(Activation(3, 3, 1.2), 10.2));
        Assert.False(receiver.Apply(Chunk(3, 1.2, 0, 1, generation: 3), 10.2));
        Assert.True(receiver.Apply(Activation(4, 4, 1.3), 10.3));
        Assert.True(receiver.TryGet(1, 10.3, out _, out _));
    }

    [Fact]
    public void Replacement_discards_incomplete_old_generation_assemblies()
    {
        var receiver = Start();
        Assert.False(receiver.Apply(Chunk(2, 1.1, 0, 2), 10.1));
        Assert.Equal(1, receiver.PendingAssemblyCount);
        Assert.True(receiver.Apply(Activation(2, 3, 1.2), 10.2));
        Assert.Equal(0, receiver.PendingAssemblyCount);
        Assert.False(receiver.Apply(Chunk(2, 1.1, 1, 2, boneId: 6), 10.21));
        Assert.Equal(0, receiver.PendingAssemblyCount);
        Assert.True(receiver.Apply(Chunk(4, 1.3, 0, 1, generation: 2), 10.3));
    }

    [Fact]
    public void Conflicting_fragment_metadata_cannot_complete_a_frame()
    {
        var receiver = Start(twoBones: true);
        Assert.False(receiver.Apply(Chunk(2, 1.1, 0, 2), 10.1));
        Assert.False(receiver.Apply(Chunk(2, 1.2, 1, 2, boneId: 6), 10.11));
        Assert.False(receiver.Apply(Chunk(2, 1.1, 1, 3, boneId: 6), 10.12));
        Assert.Equal(1, receiver.PendingAssemblyCount);
        Assert.True(receiver.TryGet(1, 10.12, out var before, out _));
        Assert.Equal(0, before.Bones[0].X);
        Assert.True(receiver.Apply(Chunk(2, 1.1, 1, 2, boneId: 6), 10.13));
        Assert.Equal(0, receiver.PendingAssemblyCount);
        Assert.True(receiver.TryGet(1, 10.3, out var completed, out _));
        Assert.All(completed.Bones, bone => Assert.Equal(1, bone.X));
    }

    [Fact]
    public void Only_two_incomplete_sequences_are_retained_and_newer_completion_discards_older_parts()
    {
        var receiver = Start(twoBones: true);
        Assert.False(receiver.Apply(Chunk(2, 1.02, 0, 2), 10.02));
        Assert.False(receiver.Apply(Chunk(3, 1.03, 0, 2), 10.03));
        Assert.False(receiver.Apply(Chunk(4, 1.04, 0, 2), 10.04));
        Assert.Equal(2, receiver.PendingAssemblyCount);
        Assert.False(receiver.Apply(Chunk(2, 1.02, 1, 2, boneId: 6), 10.05));
        Assert.Equal(2, receiver.PendingAssemblyCount);
        Assert.True(receiver.Apply(Chunk(4, 1.04, 1, 2, boneId: 6), 10.06));
        Assert.Equal(0, receiver.PendingAssemblyCount);
        Assert.False(receiver.Apply(Chunk(3, 1.03, 1, 2, boneId: 6), 10.07));
        Assert.Equal(0, receiver.PendingAssemblyCount);
    }

    [Fact]
    public void Duplicate_bones_across_fragments_are_rejected_atomically()
    {
        var receiver = Start(twoBones: true);
        Assert.False(receiver.Apply(Chunk(2, 1.1, 0, 2), 10.1));
        Assert.False(receiver.Apply(Chunk(2, 1.1, 1, 2), 10.11));
        Assert.Equal(0, receiver.PendingAssemblyCount);
        Assert.True(receiver.TryGet(1, 10.2, out var frame, out _));
        Assert.All(frame.Bones, bone => Assert.Equal(0, bone.X));
    }

    [Fact]
    public void Binding_generation_and_revision_both_prevent_rollback()
    {
        var receiver = Start();
        var next = Binding(); next.ModelGeneration = 2; next.BindingRevision = 2;
        Assert.True(receiver.Bind(next));
        var staleModel = Binding(); staleModel.BindingRevision = 3;
        Assert.False(receiver.Bind(staleModel));
        var staleRevision = Binding(); staleRevision.ModelGeneration = 3;
        Assert.False(receiver.Bind(staleRevision));
        Assert.False(receiver.Apply(Activation(), 10.1));
        Assert.False(receiver.Apply(Chunk(2, 1.1, 0, 1), 10.1));
        Assert.False(receiver.TryGet(1, 10.1, out _, out _));
    }

    private static PoseReceiver Start(bool twoBones = false)
    {
        var receiver = new PoseReceiver(); receiver.Reset(1);
        Assert.True(receiver.Bind(Binding()));
        PoseControl activation = Activation();
        if (twoBones) activation.Frame = new PoseFrame("anomaly.guard", 1, new[] { Bone(5, 0), Bone(6, 0) });
        Assert.True(receiver.Apply(activation, 10));
        return receiver;
    }

    private static PoseBinding Binding() => new() { NetId = 1, ModelGeneration = 1, BindingRevision = 1,
        RoleId = 15, RigId = "anomaly.guard", SchemaVersion = 1 };
    private static PoseBone Bone(ushort id, float x) => new(id, PoseComponents.Position | PoseComponents.Rotation,
        x, 0, 0, 0, 0, 0, 1);
    private static PoseControl Activation(ulong generation = 1, ulong sequence = 1, double time = 1) => new()
    {
        Operation = PoseControlOperation.Activate, Binding = Binding(), OverrideGeneration = generation,
        Sequence = sequence, ServerTime = time, Frame = new PoseFrame("anomaly.guard", 1, new[] { Bone(5, 0) }),
    };
    private static PoseControl Clear(ulong generation) => new() { Operation = PoseControlOperation.Clear,
        Binding = Binding(), OverrideGeneration = generation };
    private static PoseSampleChunk Chunk(ulong sequence, double time, byte index, byte count,
        ushort boneId = 5, ulong generation = 1)
    {
        var chunk = new PoseSampleChunk { NetId = 1, ModelGeneration = 1, BindingRevision = 1,
            OverrideGeneration = generation, Sequence = sequence, ServerTime = time,
            ChunkIndex = index, ChunkCount = count };
        chunk.Bones.Add(Bone(boneId, 1));
        return chunk;
    }
}
