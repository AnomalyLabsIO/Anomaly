using System;
using System.IO;
using System.Linq;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class FacilityPlacementPlanTests
{
    [Fact]
    public void Planned_snapshot_roundtrip_preserves_order_removed_slots_and_all_descriptors()
    {
        var snapshot = Create();
        var room = snapshot.Zones[0].Rooms[0];
        room.IsRemoved = true;
        snapshot.Zones[0].Rooms.Add(new FacilityPlannedRoom { CoordsX = -8, HasPlacement = true,
            CandidateIndex = room.CandidateIndex, DuplicateId = 2, Name = room.Name });
        snapshot.Zones[0].Rooms.Add(new FacilityPlannedRoom { CoordsX = 20, HasPlacement = false });
        snapshot.Seal();

        byte[] encoded = Encode(snapshot);
        var decoded = Decode(encoded);
        Assert.Equal(encoded, Encode(decoded));
        Assert.Equal(FacilityGenerationMode.Planned, decoded.Mode);
        Assert.Equal(new short[] { 7, -8, 20 }, decoded.Zones[0].Rooms.Select(value => value.CoordsX));
        Assert.True(decoded.Zones[0].Rooms[0].IsRemoved);
        Assert.Equal(2, decoded.Zones[0].Rooms[1].DuplicateId);
        Assert.False(decoded.Zones[0].Rooms[2].HasPlacement);
        Assert.Equal(new[] { 15, 3 }, decoded.Zones[0].Rooms[0].SpecificRooms);
        Assert.Equal(19.25f, decoded.Zones[2].EntranceOffsetZ);
        Assert.Equal(snapshot.Digest, decoded.Digest);
    }

    [Fact]
    public void Plan_copy_detaches_every_mutable_collection_and_digest()
    {
        var snapshot = Create();
        snapshot.Seal();
        var copy = snapshot.Copy();
        copy.Digest[0] ^= 1;
        copy.RoomsDigest[0] ^= 1;
        copy.Zones[0].CatalogDigest[0] ^= 1;
        copy.Zones[0].Rooms[0].SpecificRooms.Add(99);
        copy.Zones[0].Rooms[0].PositionX = 400;
        copy.Zones[1].Rooms.Clear();
        copy.Zones.RemoveAt(2);
        snapshot.Validate();
        Assert.Equal(3, snapshot.Zones.Count);
        Assert.Single(snapshot.Zones[1].Rooms);
        Assert.Equal(2, snapshot.Zones[0].Rooms[0].SpecificRooms.Count);
        Assert.Equal(1.25f, snapshot.Zones[0].Rooms[0].PositionX);
    }

    [Theory]
    [InlineData("room-digest")]
    [InlineData("catalog-digest")]
    [InlineData("zone-order")]
    [InlineData("room-order")]
    [InlineData("candidate")]
    [InlineData("duplicate")]
    [InlineData("removed")]
    [InlineData("placement")]
    [InlineData("specific-order")]
    [InlineData("interpretation")]
    [InlineData("transform")]
    [InlineData("entrance-offset")]
    public void Authenticated_digest_binds_plan_content_and_semantic_order(string mutation)
    {
        var snapshot = Create();
        snapshot.Zones[0].Rooms.Add(new FacilityPlannedRoom { CoordsX = 100 });
        snapshot.Seal();
        var zone = snapshot.Zones[0];
        var room = zone.Rooms[0];
        switch (mutation)
        {
            case "room-digest": snapshot.RoomsDigest[0] ^= 1; break;
            case "catalog-digest": zone.CatalogDigest[0] ^= 1; break;
            case "zone-order": snapshot.Zones.Reverse(); break;
            case "room-order": zone.Rooms.Reverse(); break;
            case "candidate": room.CandidateIndex++; break;
            case "duplicate": room.DuplicateId++; break;
            case "removed": room.IsRemoved = !room.IsRemoved; break;
            case "placement": room.HasPlacement = !room.HasPlacement; break;
            case "specific-order": room.SpecificRooms.Reverse(); break;
            case "interpretation": room.InterpretationRotationY++; break;
            case "transform": room.PositionZ++; break;
            case "entrance-offset": zone.EntranceOffsetX++; break;
            default: throw new ArgumentException(mutation);
        }
        Assert.Throws<InvalidDataException>(snapshot.Validate);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("native-plan")]
    [InlineData("missing-zone")]
    [InlineData("repeated-zone")]
    [InlineData("unsupported-zone")]
    [InlineData("null-zone")]
    [InlineData("null-rooms")]
    [InlineData("null-room")]
    [InlineData("repeated-coords")]
    [InlineData("candidate")]
    [InlineData("negative-duplicate")]
    [InlineData("large-duplicate")]
    [InlineData("room-digest")]
    [InlineData("catalog-digest")]
    [InlineData("nonfinite-offset")]
    [InlineData("nonfinite-interpretation")]
    [InlineData("nonfinite-position")]
    [InlineData("nonfinite-rotation")]
    [InlineData("null-specific")]
    [InlineData("disabled")]
    public void Invalid_plan_is_rejected_before_sealing(string invalid)
    {
        var snapshot = Create();
        var zone = snapshot.Zones[0];
        var room = zone.Rooms[0];
        switch (invalid)
        {
            case "mode": snapshot.Mode = (FacilityGenerationMode)9; break;
            case "native-plan": snapshot.Mode = FacilityGenerationMode.Native; break;
            case "missing-zone": snapshot.Zones.RemoveAt(2); break;
            case "repeated-zone": snapshot.Zones[2].ZoneId = 1; break;
            case "unsupported-zone": zone.ZoneId = 4; break;
            case "null-zone": snapshot.Zones[0] = null; break;
            case "null-rooms": zone.Rooms = null; break;
            case "null-room": zone.Rooms[0] = null; break;
            case "repeated-coords": zone.Rooms.Add(room.Copy()); break;
            case "candidate": room.CandidateIndex = -1; break;
            case "negative-duplicate": room.DuplicateId = -1; break;
            case "large-duplicate": room.DuplicateId = FacilitySnapshotMessage.MaximumPlannedRooms; break;
            case "room-digest": snapshot.RoomsDigest = new byte[31]; break;
            case "catalog-digest": zone.CatalogDigest = new byte[33]; break;
            case "nonfinite-offset": zone.EntranceOffsetZ = float.NaN; break;
            case "nonfinite-interpretation": room.InterpretationRotationY = float.NegativeInfinity; break;
            case "nonfinite-position": room.PositionY = float.PositiveInfinity; break;
            case "nonfinite-rotation": room.RotationY = float.NaN; break;
            case "null-specific": room.SpecificRooms = null; break;
            case "disabled": snapshot.Seed = -1; break;
            default: throw new ArgumentException(invalid);
        }
        Assert.Throws<InvalidDataException>(snapshot.Seal);
    }

    [Fact]
    public void Specific_room_budget_is_cumulative_across_zones()
    {
        var snapshot = Create();
        foreach (var zone in snapshot.Zones) zone.Rooms[0].SpecificRooms.Clear();
        snapshot.Zones[0].Rooms[0].SpecificRooms.AddRange(Enumerable.Repeat(1, FacilitySnapshotMessage.MaximumSpecificRooms));
        snapshot.Seal();
        snapshot.Zones[2].Rooms[0].SpecificRooms.Add(1);
        Assert.Throws<InvalidDataException>(snapshot.Seal);
    }

    [Fact]
    public void Planned_rooms_obey_the_total_payload_ceiling()
    {
        var snapshot = Create();
        snapshot.Zones[0].Rooms.Clear();
        for (short i = 0; i < 3100; i++)
            snapshot.Zones[0].Rooms.Add(new FacilityPlannedRoom { CoordsX = i, HasPlacement = true });
        Assert.Throws<InvalidDataException>(snapshot.Seal);
    }

    [Theory]
    [InlineData("zone-count")]
    [InlineData("room-count")]
    [InlineData("specific-count")]
    [InlineData("boolean")]
    [InlineData("truncated")]
    public void Malformed_wire_plan_is_rejected(string invalid)
    {
        var snapshot = Create();
        snapshot.Seal();
        byte[] bytes = Encode(snapshot);
        // Header: generation(8), seed(4), mode(1), empty recipe/atlas counts(2+2).
        const int zoneCountOffset = 17;
        const int roomCountOffset = zoneCountOffset + 1 + 1 + 32 + 12;
        const int specificCountOffset = roomCountOffset + 2 + 2 + 2 + 4 + 4;
        if (invalid == "zone-count") bytes[zoneCountOffset] = 4;
        else if (invalid == "room-count") SetCount(bytes, roomCountOffset, FacilitySnapshotMessage.MaximumPlannedRooms + 1);
        else if (invalid == "specific-count") SetCount(bytes, specificCountOffset, FacilitySnapshotMessage.MaximumSpecificRooms + 1);
        else if (invalid == "boolean") bytes[specificCountOffset + 2 + 8] = 2;
        else if (invalid == "truncated") bytes = bytes.Take(bytes.Length - 1).ToArray();
        else throw new ArgumentException(invalid);
        Assert.Throws<InvalidDataException>(() => Decode(bytes));
    }

    private static FacilitySnapshotMessage Create()
    {
        var snapshot = new FacilitySnapshotMessage { GenerationId = 11, Seed = 123, Mode = FacilityGenerationMode.Planned };
        snapshot.RoomsDigest[2] = 19;
        for (byte zoneId = 1; zoneId <= 3; zoneId++)
        {
            var zone = new FacilityZonePlan { ZoneId = zoneId, EntranceOffsetZ = 19.25f };
            zone.CatalogDigest[3] = zoneId;
            zone.Rooms.Add(new FacilityPlannedRoom { CoordsX = 7, CoordsY = -9, Shape = 2,
                InterpretationRotationY = 90f, SpecificRooms = { 15, 3 }, HasPlacement = true,
                CandidateIndex = 8, Name = 15, DuplicateId = 1,
                PositionX = 1.25f, PositionY = -2f, PositionZ = 4.5f, RotationY = 270f });
            snapshot.Zones.Add(zone);
        }
        return snapshot;
    }

    private static byte[] Encode(FacilitySnapshotMessage snapshot)
    {
        using var stream = new MemoryStream();
        snapshot.Serialize(new BinaryWriter(stream));
        return stream.ToArray();
    }

    private static FacilitySnapshotMessage Decode(byte[] encoded)
    {
        using var stream = new MemoryStream(encoded);
        var result = new FacilitySnapshotMessage();
        result.Deserialize(new BinaryReader(stream));
        Assert.Equal(stream.Length, stream.Position);
        return result;
    }

    private static void SetCount(byte[] bytes, int offset, int count)
    {
        bytes[offset] = (byte)count;
        bytes[offset + 1] = (byte)(count >> 8);
    }
}
