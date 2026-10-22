using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Poses;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class PlayerPoseCodecTests
{
    [Fact]
    public void Control_roundtrip_preserves_complete_binding_and_quantized_pose()
    {
        var input = new PlayerPoseControlMessage { RoundEpoch = 7 };
        input.Controls.Add(new PoseControl { Operation = PoseControlOperation.Bind, Binding = Binding() });
        input.Controls.Add(new PoseControl { Operation = PoseControlOperation.Activate, Binding = Binding(), OverrideGeneration = 4,
            Sequence = 6, ServerTime = 12.25, Frame = new PoseFrame("anomaly.guard", 1, new[] { Bone() }) });
        input.Controls.Add(new PoseControl { Operation = PoseControlOperation.Clear, Binding = Binding(), OverrideGeneration = 5 });
        var output = RoundTrip<PlayerPoseControlMessage>(input);
        Assert.Equal(7u, output.RoundEpoch);
        Assert.Equal(3, output.Controls.Count);
        Assert.True(input.Controls[1].Binding.Matches(output.Controls[1].Binding));
        var decoded = output.Controls[1].Frame.Bones[0];
        Assert.InRange(Math.Abs(decoded.X - Bone().X), 0, 0.000501f);
        Assert.InRange(Math.Abs(decoded.Weight - Bone().Weight), 0, 1f / 255f);
        Assert.InRange(Math.Abs(decoded.Qy - Bone().Qy), 0, 0.0001f);
        Assert.True(decoded.IncludeLocalPlayer);
    }

    [Fact]
    public void Size_calculator_matches_actual_sample_encoding()
    {
        var sample = new PlayerPoseSampleMessage { RoundEpoch = 1 };
        var chunk = new PoseSampleChunk { NetId = 1, ModelGeneration = 2, BindingRevision = 3, OverrideGeneration = 4,
            Sequence = 5, ServerTime = 6, ChunkCount = 1 };
        chunk.Bones.Add(Bone());
        chunk.Bones.Add(new PoseBone(6, PoseComponents.Rotation, 0, 0, 0, 0, 0, 0, 1));
        sample.Chunks.Add(chunk);
        using (var stream = new MemoryStream())
        {
            sample.Serialize(new BinaryWriter(stream));
            Assert.Equal(PoseWireCodec.MessageHeaderBytes + PoseWireCodec.GetSampleChunkSize(chunk), stream.Length);
        }
        var output = RoundTrip<PlayerPoseSampleMessage>(sample);
        Assert.Equal(2, output.Chunks[0].Bones.Count);
    }

    [Fact]
    public void Frame_is_detached_normalized_and_rejects_duplicate_or_default_bones()
    {
        var bones = new List<PoseBone> { Bone() };
        var frame = new PoseFrame("anomaly.guard", 1, bones);
        bones.Clear(); Assert.Single(frame.Bones);
        var b = frame.Bones[0];
        Assert.InRange(b.Qx*b.Qx + b.Qy*b.Qy + b.Qz*b.Qz + b.Qw*b.Qw, 0.99999f, 1.00001f);
        Assert.Throws<ArgumentException>(() => new PoseFrame("anomaly.guard", 1, new[] { Bone(), Bone() }));
        Assert.ThrowsAny<ArgumentException>(() => new PoseFrame("anomaly.guard", 1, new[] { default(PoseBone) }));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(327.68f)]
    [InlineData(-327.68f)]
    public void Invalid_positions_are_rejected(float x) =>
        Assert.Throws<ArgumentException>(() => new PoseBone(1, PoseComponents.Position, x, 0, 0, 0, 0, 0, 1));

    [Fact]
    public void Every_truncation_is_rejected_without_unbounded_reads()
    {
        var input = new PlayerPoseControlMessage { RoundEpoch = 1 };
        input.Controls.Add(new PoseControl { Operation = PoseControlOperation.Activate, Binding = Binding(), OverrideGeneration = 1,
            Sequence = 1, ServerTime = 1, Frame = new PoseFrame("anomaly.guard", 1, new[] { Bone() }) });
        byte[] bytes;
        using (var stream = new MemoryStream()) { input.Serialize(new BinaryWriter(stream)); bytes = stream.ToArray(); }
        for (int length = 0; length < bytes.Length; length++)
        {
            byte[] truncated = bytes.Take(length).ToArray();
            Assert.ThrowsAny<Exception>(() => new PlayerPoseControlMessage().Deserialize(new BinaryReader(new MemoryStream(truncated))));
        }
    }

    [Fact]
    public void Oversized_chunk_and_bone_counts_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => PoseWireCodec.ValidateChunk(new PoseSampleChunk { NetId = 1,
            ModelGeneration = 1, BindingRevision = 1, OverrideGeneration = 1, Sequence = 1, ChunkCount = 17 }));
        using (var stream = new MemoryStream())
        {
            new BinaryWriter(stream).Write((ushort)129); stream.Position = 0;
            Assert.Throws<InvalidDataException>(() => PoseWireCodec.ReadBones(new BinaryReader(stream)));
        }
    }

    [Fact]
    public void Catalog_has_eleven_immutable_distinct_bounded_schemas()
    {
        Assert.Equal(11, PoseRigCatalog.All.Count);
        foreach (var rig in PoseRigCatalog.All)
        {
            Assert.InRange(rig.Bones.Count, 1, 128);
            Assert.Equal(rig.Bones.Count, rig.Bones.Select(b => b.Id).Distinct().Count());
            Assert.All(rig.Bones, b => Assert.InRange(b.Id, (ushort)0, (ushort)127));
            Assert.True(PoseRigCatalog.TryGet(rig.Id, rig.Version, out var found)); Assert.Same(rig, found);
        }
        Assert.False(PoseRigCatalog.TryGetForRole(0, out _)); // SCP-173 has no supported articulated rig.
    }

    private static PoseBinding Binding() => new PoseBinding { NetId = 1, ModelGeneration = 2, BindingRevision = 3,
        RoleId = 15, RigId = "anomaly.guard", SchemaVersion = 1 };
    private static PoseBone Bone() => new PoseBone(5, PoseComponents.Position | PoseComponents.Rotation,
        0.12345f, -1.23456f, 0.9f, 0, 1, 0, 1, 0.73f, true);
    private static T RoundTrip<T>(IAnomalyMessage input) where T : IAnomalyMessage, new()
    {
        using (var stream = new MemoryStream())
        {
            input.Serialize(new BinaryWriter(stream)); stream.Position = 0;
            var output = new T(); output.Deserialize(new BinaryReader(stream)); Assert.Equal(stream.Length, stream.Position); return output;
        }
    }
}
