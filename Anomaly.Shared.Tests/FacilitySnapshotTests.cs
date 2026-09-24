using System;
using System.IO;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class FacilitySnapshotTests
{
    [Fact]
    public void Full_snapshot_roundtrip_binds_seed_generation_room_and_pixel_data()
    {
        var snapshot = CreatePlanned();
        snapshot.Recipe.Add(new FacilityRecipeEntry { ZoneId = 1, CoordsX = 7, CoordsY = 9,
            DirtyMask = 3, OffsetX = 1.5f, OffsetY = -2f, OffsetZ = 3f, RotationY = 270f });
        snapshot.AtlasEdits.Add(new AtlasPixelEdit { ZoneId = 2, AtlasIdx = 3, PixelX = 17, PixelY = 21, R = 23, G = 42, B = 91 });
        snapshot.Seal();
        using var stream = new MemoryStream();
        snapshot.Serialize(new BinaryWriter(stream));
        stream.Position = 0;
        var decoded = new FacilitySnapshotMessage();
        decoded.Deserialize(new BinaryReader(stream));
        Assert.Equal(stream.Length, stream.Position);
        Assert.Equal(snapshot.GenerationId, decoded.GenerationId);
        Assert.Equal(snapshot.Seed, decoded.Seed);
        Assert.Equal(snapshot.Digest, decoded.Digest);
        Assert.Equal(270f, decoded.Recipe[0].RotationY);
        Assert.Equal(91, decoded.AtlasEdits[0].B);
        decoded.Seed++;
        Assert.Throws<InvalidDataException>(decoded.Validate);
    }

    [Fact]
    public void Maximum_full_snapshot_fits_its_declared_ceiling()
    {
        var snapshot = CreatePlanned();
        for (short i = 0; i < 4096; i++)
        {
            snapshot.Recipe.Add(new FacilityRecipeEntry { ZoneId = 1, CoordsX = i, DirtyMask = 3, OffsetX = i, RotationY = i });
            snapshot.AtlasEdits.Add(new AtlasPixelEdit { ZoneId = 1, PixelX = (ushort)i });
        }
        snapshot.Seal();
        using var stream = new MemoryStream();
        snapshot.Serialize(new BinaryWriter(stream));
        Assert.InRange(stream.Length, 131000, snapshot.MaxPayloadBytes);
        snapshot.Recipe.Add(new FacilityRecipeEntry { ZoneId = 1, CoordsX = 4096 });
        Assert.Throws<InvalidDataException>(snapshot.Seal);
    }

    [Fact]
    public void Invalid_duplicate_and_nonfinite_recipes_are_rejected_before_commit()
    {
        var snapshot = CreatePlanned();
        snapshot.Recipe.Add(new FacilityRecipeEntry { ZoneId = 1, DirtyMask = 1, OffsetX = float.NaN });
        Assert.Throws<InvalidDataException>(snapshot.Seal);
        snapshot.Recipe.Clear();
        snapshot.Recipe.Add(new FacilityRecipeEntry { ZoneId = 1 });
        snapshot.Recipe.Add(new FacilityRecipeEntry { ZoneId = 1 });
        Assert.Throws<InvalidDataException>(snapshot.Seal);
        snapshot.Recipe.Clear();
        snapshot.AtlasEdits.Add(new AtlasPixelEdit { ZoneId = 1 });
        snapshot.AtlasEdits.Add(new AtlasPixelEdit { ZoneId = 1 });
        Assert.Throws<InvalidDataException>(snapshot.Seal);
        snapshot.AtlasEdits.Clear();
        snapshot.Recipe.Add(new FacilityRecipeEntry { ZoneId = 4 });
        Assert.Throws<InvalidDataException>(snapshot.Seal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Native_mode_rejects_mutations_that_native_generation_would_ignore(bool roomRecipe)
    {
        var snapshot = Create();
        if (roomRecipe) snapshot.Recipe.Add(new FacilityRecipeEntry { ZoneId = 1 });
        else snapshot.AtlasEdits.Add(new AtlasPixelEdit { ZoneId = 1 });
        var exception = Assert.Throws<InvalidDataException>(snapshot.Seal);
        Assert.Contains("Native facility generation", exception.Message);
    }

    [Fact]
    public void Snapshot_copy_does_not_share_mutable_state()
    {
        var original = Create();
        var copy = original.Copy();
        copy.Digest[0] ^= 1;
        copy.Recipe.Add(new FacilityRecipeEntry { ZoneId = 1 });
        original.Validate();
        Assert.Empty(original.Recipe);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Generation_waits_for_both_authenticated_snapshot_and_matching_seed(bool snapshotFirst)
    {
        var gate = new FacilityGenerationGate();
        var snapshot = Create();
        if (snapshotFirst) gate.ReceiveSnapshot(snapshot); else gate.ReceiveSeed(snapshot.Seed);
        Assert.False(gate.TryBegin());
        if (snapshotFirst) gate.ReceiveSeed(snapshot.Seed); else gate.ReceiveSnapshot(snapshot);
        Assert.True(gate.TryBegin());
        Assert.False(gate.TryBegin());
        gate.Complete();
        Assert.True(gate.Completed);
        Assert.False(gate.TryBegin());
    }

    [Fact]
    public void Duplicate_seed_and_snapshot_do_not_revoke_completed_generation()
    {
        var gate = new FacilityGenerationGate();
        var snapshot = Create();
        gate.ReceiveSeed(snapshot.Seed); gate.ReceiveSnapshot(snapshot);
        Assert.True(gate.TryBegin()); gate.Complete();
        gate.ReceiveSeed(snapshot.Seed); gate.ReceiveSnapshot(snapshot);
        Assert.True(gate.Completed);
        Assert.False(gate.TryBegin());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void New_generation_supports_same_seed_and_both_arrival_orders(bool snapshotFirst)
    {
        var gate = new FacilityGenerationGate();
        var snapshot = Create();
        gate.ReceiveSeed(snapshot.Seed); gate.ReceiveSnapshot(snapshot);
        Assert.True(gate.TryBegin()); gate.Complete();
        var next = Create(2);
        if (snapshotFirst) gate.ReceiveSnapshot(next); else gate.ReceiveSeed(next.Seed);
        Assert.False(gate.TryBegin());
        if (snapshotFirst) gate.ReceiveSeed(next.Seed); else gate.ReceiveSnapshot(next);
        Assert.True(gate.TryBegin()); gate.Complete();
        Assert.Equal(2ul, gate.Snapshot.GenerationId);
    }

    [Fact]
    public void Gate_rejects_mismatches_stale_generation_and_mid_generation_replacement()
    {
        var gate = new FacilityGenerationGate();
        gate.ReceiveSeed(999);
        Assert.Throws<InvalidDataException>(() => gate.ReceiveSnapshot(Create()));
        gate.Reset();
        gate.ReceiveSeed(123); gate.ReceiveSnapshot(Create(2));
        Assert.Throws<InvalidDataException>(() => gate.ReceiveSnapshot(Create(1)));
        Assert.True(gate.TryBegin());
        Assert.Throws<InvalidDataException>(() => gate.ReceiveSnapshot(Create(3)));
        gate.Complete(); gate.Reset();
        gate.ReceiveSnapshot(Create(1)); gate.ReceiveSeed(123);
        Assert.True(gate.TryBegin());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Scene_transition_preserves_pending_next_generation_and_requires_new_snapshot(bool snapshotBeforeSceneEnd, bool seedBeforeSceneEnd)
    {
        var gate = new FacilityGenerationGate();
        gate.ReceiveSnapshot(Create()); gate.ReceiveSeed(123);
        Assert.True(gate.TryBegin()); gate.Complete();
        var next = Create(2);
        if (snapshotBeforeSceneEnd) gate.ReceiveSnapshot(next);
        if (seedBeforeSceneEnd) gate.ReceiveSeed(next.Seed);
        gate.EndScene();
        Assert.False(gate.Completed);
        if (!snapshotBeforeSceneEnd)
        {
            Assert.Null(gate.Snapshot);
            Assert.False(gate.TryBegin());
            gate.ReceiveSnapshot(next);
        }
        if (!seedBeforeSceneEnd)
        {
            Assert.False(gate.TryBegin());
            gate.ReceiveSeed(next.Seed);
        }
        Assert.True(gate.TryBegin()); gate.Complete();
        Assert.Equal(2ul, gate.Snapshot.GenerationId);
    }

    [Fact]
    public void Missing_bootstrap_deadline_is_latched_and_resets_after_completion_and_scene_transition()
    {
        var gate = new FacilityGenerationGate();
        Assert.False(gate.HasTimedOut(1000));
        gate.BeginWaiting(100);
        gate.BeginWaiting(120);
        Assert.False(gate.HasTimedOut(129.999));
        Assert.True(gate.HasTimedOut(130));
        gate.ReceiveSeed(123); gate.ReceiveSnapshot(Create());
        Assert.True(gate.TryBegin()); gate.Complete();
        Assert.False(gate.HasTimedOut(1000));
        gate.EndScene(); gate.BeginWaiting(200);
        Assert.False(gate.HasTimedOut(229));
        Assert.True(gate.HasTimedOut(230));
        gate.Reset();
        Assert.False(gate.HasTimedOut(1000));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Duplicate_previous_seed_does_not_reject_next_generation_with_different_seed(bool sceneEndsFirst)
    {
        var gate = new FacilityGenerationGate();
        gate.ReceiveSnapshot(Create()); gate.ReceiveSeed(123);
        Assert.True(gate.TryBegin()); gate.Complete();
        gate.ReceiveSeed(123);
        if (sceneEndsFirst) gate.EndScene();
        var next = Create(2); next.Seed = 456; next.Seal();
        gate.ReceiveSnapshot(next);
        Assert.False(gate.TryBegin());
        gate.ReceiveSeed(456);
        Assert.True(gate.TryBegin());
    }

    [Fact]
    public void Pending_authenticated_bootstrap_deadline_survives_scene_teardown()
    {
        var gate = new FacilityGenerationGate();
        gate.BeginWaiting(100);
        gate.ReceiveSnapshot(Create());
        gate.EndScene();
        gate.BeginWaiting(120);
        Assert.True(gate.HasTimedOut(130));
    }

    private static FacilitySnapshotMessage Create(ulong id = 1)
    {
        var snapshot = new FacilitySnapshotMessage { GenerationId = id, Seed = 123 };
        snapshot.Seal();
        return snapshot;
    }

    private static FacilitySnapshotMessage CreatePlanned()
    {
        var snapshot = new FacilitySnapshotMessage { GenerationId = 1, Seed = 123, Mode = FacilityGenerationMode.Planned };
        for (byte zone = 1; zone <= 3; zone++) snapshot.Zones.Add(new FacilityZonePlan { ZoneId = zone });
        snapshot.Seal();
        return snapshot;
    }
}
