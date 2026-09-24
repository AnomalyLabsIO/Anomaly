using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Anomaly.Shared.Networking.Messages;

/// <summary>One authenticated, atomic pre-generation facility description.</summary>
public sealed class FacilitySnapshotMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
{
    public const int MaximumPayloadBytes = 132 * 1024;
    public const int MaximumEntries = 4096;
    public const int MaximumAtlasEdits = 4096;
    public const int MaximumPlannedRooms = 4096;
    public const int MaximumSpecificRooms = 4096;
    public override int MaxPayloadBytes => MaximumPayloadBytes;
    public string MessageName => "anomaly:facility:snapshot";
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public ulong GenerationId { get; set; }
    public int Seed { get; set; }
    public FacilityGenerationMode Mode { get; set; }
    public List<FacilityRecipeEntry> Recipe { get; set; } = new List<FacilityRecipeEntry>();
    public List<AtlasPixelEdit> AtlasEdits { get; set; } = new List<AtlasPixelEdit>();
    public List<FacilityZonePlan> Zones { get; set; } = new List<FacilityZonePlan>();
    public byte[] RoomsDigest { get; set; } = new byte[32];
    public byte[] Digest { get; set; }
    public bool HasMutations => Recipe.Count != 0 || AtlasEdits.Count != 0;

    public void Seal()
    {
        if (Recipe == null || AtlasEdits == null || Zones == null)
            throw new InvalidDataException("Facility snapshot collections cannot be null.");
        Recipe = Recipe.OrderBy(e => e.ZoneId).ThenBy(e => e.CoordsX).ThenBy(e => e.CoordsY).ToList();
        AtlasEdits = AtlasEdits.OrderBy(e => e.ZoneId).ThenBy(e => e.AtlasIdx)
            .ThenBy(e => e.PixelX).ThenBy(e => e.PixelY).ToList();
        Digest = ComputeDigest();
    }

    public FacilitySnapshotMessage Copy() => new FacilitySnapshotMessage
    {
        GenerationId = GenerationId, Seed = Seed, Mode = Mode,
        Recipe = new List<FacilityRecipeEntry>(Recipe),
        AtlasEdits = new List<AtlasPixelEdit>(AtlasEdits),
        Zones = Zones?.Select(zone => zone?.Copy()).ToList(),
        RoomsDigest = RoomsDigest == null ? null : (byte[])RoomsDigest.Clone(),
        Digest = Digest == null ? null : (byte[])Digest.Clone()
    };

    public void Validate()
    {
        byte[] expected = ComputeDigest();
        if (Digest == null || Digest.Length != 32)
            throw new InvalidDataException("Facility snapshot digest must be 32 bytes.");
        int difference = 0;
        for (int i = 0; i < expected.Length; i++) difference |= expected[i] ^ Digest[i];
        if (difference != 0) throw new InvalidDataException("Facility snapshot digest did not match its contents.");
    }

    public void Serialize(BinaryWriter writer)
    {
        Validate();
        WriteContent(writer);
        writer.Write(Digest);
    }

    public void Deserialize(BinaryReader reader)
    {
        GenerationId = reader.ReadUInt64();
        Seed = reader.ReadInt32();
        Mode = (FacilityGenerationMode)reader.ReadByte();
        var recipe = new FacilityRecipeCodec();
        recipe.Deserialize(reader);
        Recipe = recipe.Entries;
        var edits = new FacilityAtlasEditsCodec();
        edits.Deserialize(reader);
        AtlasEdits = edits.Edits;
        Zones = ReadZones(reader);
        RoomsDigest = reader.ReadBytes(32);
        Digest = reader.ReadBytes(32);
        Validate();
    }

    private byte[] ComputeDigest()
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            WriteContent(writer);
            writer.Flush();
            if (stream.Length + 32 > MaximumPayloadBytes)
                throw new InvalidDataException("Facility snapshot exceeded its payload limit.");
            using (var sha = SHA256.Create()) return sha.ComputeHash(stream.ToArray());
        }
    }

    private void WriteContent(BinaryWriter writer)
    {
        if (GenerationId == 0 || (Seed != -1 && Seed < 1))
            throw new InvalidDataException("Facility snapshot generation or seed was invalid.");
        if (Recipe == null || Recipe.Count > MaximumEntries || AtlasEdits == null || AtlasEdits.Count > MaximumAtlasEdits)
            throw new InvalidDataException("Facility snapshot exceeded its entry limit.");
        if (Mode != FacilityGenerationMode.Native && Mode != FacilityGenerationMode.Planned)
            throw new InvalidDataException("Facility generation mode was invalid.");
        if (Mode == FacilityGenerationMode.Native && HasMutations)
            throw new InvalidDataException("Native facility generation cannot carry room recipes or atlas edits.");
        if (RoomsDigest == null || RoomsDigest.Length != 32)
            throw new InvalidDataException("Facility room digest must be 32 bytes.");
        ValidatePlan();
        var rooms = new HashSet<(byte, short, short)>();
        foreach (FacilityRecipeEntry entry in Recipe)
        {
            ValidateZone(entry.ZoneId);
            if (!rooms.Add((entry.ZoneId, entry.CoordsX, entry.CoordsY)))
                throw new InvalidDataException("Facility snapshot repeated a room coordinate.");
        }
        var pixels = new HashSet<(byte, byte, ushort, ushort)>();
        foreach (AtlasPixelEdit edit in AtlasEdits)
        {
            ValidateZone(edit.ZoneId);
            if (!pixels.Add((edit.ZoneId, edit.AtlasIdx, edit.PixelX, edit.PixelY)))
                throw new InvalidDataException("Facility snapshot repeated an atlas pixel.");
        }
        if (Seed == -1 && (Recipe.Count != 0 || AtlasEdits.Count != 0 || Mode != FacilityGenerationMode.Native))
            throw new InvalidDataException("Disabled facility generation cannot carry mutations.");
        writer.Write(GenerationId);
        writer.Write(Seed);
        writer.Write((byte)Mode);
        new FacilityRecipeCodec { Entries = Recipe }.Serialize(writer);
        new FacilityAtlasEditsCodec { Edits = AtlasEdits }.Serialize(writer);
        WriteZones(writer);
        writer.Write(RoomsDigest);
    }

    private void ValidatePlan()
    {
        if (Zones == null || Zones.Count != (Mode == FacilityGenerationMode.Planned ? 3 : 0))
            throw new InvalidDataException("Planned facility generation requires all three atlas zones; native generation cannot carry a plan.");
        var seenZones = new HashSet<byte>();
        int roomCount = 0;
        int specificCount = 0;
        foreach (var zone in Zones)
        {
            if (zone == null) throw new InvalidDataException("Facility plan zone cannot be null.");
            ValidateZone(zone.ZoneId);
            if (!seenZones.Add(zone.ZoneId)) throw new InvalidDataException("Facility plan repeated a zone.");
            if (zone.CatalogDigest == null || zone.CatalogDigest.Length != 32)
                throw new InvalidDataException("Facility catalog digest must be 32 bytes.");
            if (!IsFinite(zone.EntranceOffsetX) || !IsFinite(zone.EntranceOffsetY) || !IsFinite(zone.EntranceOffsetZ))
                throw new InvalidDataException("Facility entrance offset contained a non-finite value.");
            if (zone.Rooms == null || zone.Rooms.Count > MaximumPlannedRooms - roomCount)
                throw new InvalidDataException("Facility plan exceeded its room limit.");
            roomCount += zone.Rooms.Count;
            var coords = new HashSet<(short, short)>();
            foreach (var room in zone.Rooms)
            {
                if (room == null) throw new InvalidDataException("Facility planned room cannot be null.");
                if (!coords.Add((room.CoordsX, room.CoordsY)))
                    throw new InvalidDataException("Facility plan repeated a room coordinate.");
                if (room.SpecificRooms == null || room.SpecificRooms.Count > MaximumSpecificRooms - specificCount)
                    throw new InvalidDataException("Facility plan exceeded its specific-room limit.");
                specificCount += room.SpecificRooms.Count;
                if (room.HasPlacement && room.CandidateIndex < 0)
                    throw new InvalidDataException("Facility planned candidate index was invalid.");
                if (room.DuplicateId < 0 || room.DuplicateId >= MaximumPlannedRooms)
                    throw new InvalidDataException("Facility planned duplicate ordinal was invalid.");
                if (!IsFinite(room.InterpretationRotationY) || !IsFinite(room.PositionX) ||
                    !IsFinite(room.PositionY) || !IsFinite(room.PositionZ) || !IsFinite(room.RotationY))
                    throw new InvalidDataException("Facility planned transform contained a non-finite value.");
            }
        }
    }

    private void WriteZones(BinaryWriter writer)
    {
        writer.Write((byte)Zones.Count);
        foreach (var zone in Zones)
        {
            writer.Write(zone.ZoneId);
            writer.Write(zone.CatalogDigest);
            writer.Write(zone.EntranceOffsetX); writer.Write(zone.EntranceOffsetY); writer.Write(zone.EntranceOffsetZ);
            writer.Write((ushort)zone.Rooms.Count);
            foreach (var room in zone.Rooms)
            {
                writer.Write(room.CoordsX); writer.Write(room.CoordsY);
                writer.Write(room.Shape); writer.Write(room.InterpretationRotationY);
                writer.Write((ushort)room.SpecificRooms.Count);
                foreach (int specific in room.SpecificRooms) writer.Write(specific);
                writer.Write(room.HasPlacement);
                writer.Write(room.CandidateIndex); writer.Write(room.Name); writer.Write(room.DuplicateId);
                writer.Write(room.PositionX); writer.Write(room.PositionY); writer.Write(room.PositionZ); writer.Write(room.RotationY);
                writer.Write(room.IsRemoved);
            }
        }
    }

    private static List<FacilityZonePlan> ReadZones(BinaryReader reader)
    {
        int zoneCount = reader.ReadByte();
        if (zoneCount > 3) throw new InvalidDataException("Facility plan exceeded its zone limit.");
        var zones = new List<FacilityZonePlan>(zoneCount);
        int totalRooms = 0;
        int totalSpecific = 0;
        for (int i = 0; i < zoneCount; i++)
        {
            var zone = new FacilityZonePlan
            {
                ZoneId = reader.ReadByte(), CatalogDigest = reader.ReadBytes(32),
                EntranceOffsetX = reader.ReadSingle(), EntranceOffsetY = reader.ReadSingle(), EntranceOffsetZ = reader.ReadSingle(),
            };
            int roomCount = reader.ReadUInt16();
            if (roomCount > MaximumPlannedRooms - totalRooms)
                throw new InvalidDataException("Facility plan exceeded its room limit.");
            totalRooms += roomCount;
            for (int j = 0; j < roomCount; j++)
            {
                var room = new FacilityPlannedRoom
                {
                    CoordsX = reader.ReadInt16(), CoordsY = reader.ReadInt16(),
                    Shape = reader.ReadInt32(), InterpretationRotationY = reader.ReadSingle(),
                };
                int specificCount = reader.ReadUInt16();
                if (specificCount > MaximumSpecificRooms - totalSpecific)
                    throw new InvalidDataException("Facility plan exceeded its specific-room limit.");
                totalSpecific += specificCount;
                for (int k = 0; k < specificCount; k++) room.SpecificRooms.Add(reader.ReadInt32());
                room.HasPlacement = ReadBoolean(reader);
                room.CandidateIndex = reader.ReadInt32(); room.Name = reader.ReadInt32(); room.DuplicateId = reader.ReadInt32();
                room.PositionX = reader.ReadSingle(); room.PositionY = reader.ReadSingle(); room.PositionZ = reader.ReadSingle(); room.RotationY = reader.ReadSingle();
                room.IsRemoved = ReadBoolean(reader);
                zone.Rooms.Add(room);
            }
            zones.Add(zone);
        }
        return zones;
    }

    private static bool ReadBoolean(BinaryReader reader)
    {
        byte value = reader.ReadByte();
        if (value > 1) throw new InvalidDataException("Facility plan boolean was invalid.");
        return value != 0;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static void ValidateZone(byte zone)
    {
        // Only the three atlas-based facility zones support this contract.
        if (zone < 1 || zone > 3) throw new InvalidDataException("Unsupported facility atlas zone.");
    }
}
