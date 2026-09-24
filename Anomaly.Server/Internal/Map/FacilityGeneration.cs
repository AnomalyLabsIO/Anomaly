using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Globalization;
using Anomaly.Shared.Networking.Messages;
using UnityEngine;
#if ANOMALY_CLIENT
using Il2CppMapGeneration;
using Il2CppMapGeneration.Holidays;
using NetworkIdentity = Il2CppMirror.NetworkIdentity;
using Random = Il2CppSystem.Random;
namespace Anomaly.Client.Internal.Map;
#else
using MapGeneration;
using MapGeneration.Holidays;
using NetworkIdentity = Mirror.NetworkIdentity;
using Random = System.Random;
namespace Anomaly.Server.Internal.Map;
#endif

/// <summary>
/// Compiled into both runtimes. Mirrors the supported game's base AtlasZoneGenerator
/// pipeline without installing reverse IL2CPP trampolines for AtlasInterpretation.
/// Derived zone generators retain their native pre/post-generation behavior.
/// </summary>
internal static class FacilityGeneration
{
    internal static void ValidateAssets(FacilitySnapshotMessage snapshot)
    {
        var generators = UnityEngine.Object.FindObjectsByType<AtlasZoneGenerator>(FindObjectsSortMode.None);
        foreach (var zone in snapshot.Zones)
        {
            var generator = FindGenerator(generators, zone.ZoneId);
            if (!EqualDigest(zone.CatalogDigest, CatalogDigest(generator)))
                throw new InvalidDataException("Facility prefab catalog differs in zone " + generator.TargetZone + ".");
            var occurrences = new Dictionary<int, int>();
            foreach (var room in zone.Rooms)
            {
                if (!room.HasPlacement) continue;
                if (room.CandidateIndex < 0 || room.CandidateIndex >= generator.CompatibleRooms.Length)
                    throw new InvalidDataException("Facility plan references an unavailable room prefab.");
                var candidate = EffectiveCandidate(generator.CompatibleRooms[room.CandidateIndex]);
                if ((int)candidate.Room.Name != room.Name || (int)candidate.Room.Shape != room.Shape)
                    throw new InvalidDataException("Facility plan room metadata differs from its prefab.");
                int candidateId = candidate.GetInstanceID();
                occurrences.TryGetValue(candidateId, out int occurrence);
                if (room.DuplicateId != occurrence || occurrence >= candidate.MaxAmount ||
                    (!room.IsRemoved && occurrence >= 12 && candidate.GetComponentsInChildren<NetworkIdentity>(true).Length != 0))
                    throw new InvalidDataException("Facility plan room occurrence exceeds its reserved identity range.");
                occurrences[candidateId] = occurrence + 1;
            }
        }
        foreach (var entry in snapshot.Recipe)
            FindGenerator(generators, entry.ZoneId);
        foreach (var edit in snapshot.AtlasEdits)
        {
            var generator = FindGenerator(generators, edit.ZoneId);
            if (generator.Atlases == null || edit.AtlasIdx >= generator.Atlases.Length)
                throw new InvalidDataException("Facility edit targets an unavailable atlas.");
            var atlas = generator.Atlases[edit.AtlasIdx];
            if (atlas == null || edit.PixelX >= atlas.width || edit.PixelY >= atlas.height)
                throw new InvalidDataException("Facility edit exceeds the actual atlas dimensions.");
        }
    }

    private static AtlasZoneGenerator FindGenerator(IEnumerable<AtlasZoneGenerator> generators, byte zone)
    {
        foreach (var generator in generators)
            if ((byte)generator.TargetZone == zone) return generator;
        throw new InvalidDataException("Facility snapshot targets an unavailable zone generator.");
    }

#if !ANOMALY_CLIENT
    internal static FacilityZonePlan Plan(AtlasZoneGenerator generator, Random rng, FacilitySnapshotMessage snapshot)
    {
        if (generator.Spawned.Count != 0)
            throw new InvalidOperationException("Facility planning must precede room instantiation.");
        var plan = new FacilityZonePlan { ZoneId = (byte)generator.TargetZone, CatalogDigest = CatalogDigest(generator) };
        var originals = new Dictionary<int, Texture2D>();
        var clones = new List<Texture2D>();
        try
        {
            foreach (var edit in snapshot.AtlasEdits)
            {
                if (edit.ZoneId != (byte)generator.TargetZone) continue;
                if (!originals.ContainsKey(edit.AtlasIdx))
                {
                    Texture2D source = generator.Atlases[edit.AtlasIdx];
                    originals.Add(edit.AtlasIdx, source);
                    var clone = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true)
                    {
                        filterMode = source.filterMode, wrapMode = source.wrapMode, anisoLevel = source.anisoLevel
                    };
                    clones.Add(clone);
                    clone.SetPixels32(source.GetPixels32());
                    generator.Atlases[edit.AtlasIdx] = clone;
                }
                generator.Atlases[edit.AtlasIdx].SetPixel(edit.PixelX, edit.PixelY, new Color32(edit.R, edit.G, edit.B, 255));
            }
            foreach (var clone in clones) clone.Apply(false, false);

            Texture2D atlas = generator.Atlases[rng.Next(generator.Atlases.Length)];
            generator.Interpreted = MapAtlasInterpreter.Singleton.Interpret(atlas, rng);
            generator.RandomizeInterpreted(rng);
            var entrance = generator as EntranceZoneGenerator;
            // The native private OnBeforeRandomize may be inlined. Apply this
            // after its virtual parent returns instead of detouring that helper.
            if (entrance != null)
            {
                AdjustEntrance(entrance, snapshot);
                plan.EntranceOffsetX = entrance._positionOffset.x;
                plan.EntranceOffsetY = entrance._positionOffset.y;
                plan.EntranceOffsetZ = entrance._positionOffset.z;
            }
            var selected = new List<(AtlasInterpretation Interpretation, SpawnableRoom Candidate)>();
            foreach (var interpretation in generator.Interpreted)
            {
                var record = new FacilityPlannedRoom
                {
                    CoordsX = checked((short)interpretation.Coords.x), CoordsY = checked((short)interpretation.Coords.y),
                    Shape = (int)interpretation.RoomShape, InterpretationRotationY = interpretation.RotationY,
                };
                foreach (var name in interpretation.SpecificRooms) record.SpecificRooms.Add((int)name);
                plan.Rooms.Add(record);
                bool hasRecipe = TryGet(snapshot, generator.TargetZone, interpretation.Coords, out var entry);
                if (hasRecipe && entry.OpType == FacilityRecipeCodec.OpSkip) continue;
                int index = SelectCandidate(generator, interpretation, rng, selected);
                var candidate = EffectiveCandidate(generator.CompatibleRooms[index]);
                generator.GetPositionAndRotation(interpretation, out Vector3 position, out float rotation);
                if (hasRecipe)
                {
                    if ((entry.DirtyMask & FacilityRecipeCodec.DirtyPosition) != 0)
                        position += new Vector3(entry.OffsetX, entry.OffsetY, entry.OffsetZ);
                    if ((entry.DirtyMask & FacilityRecipeCodec.DirtyRotation) != 0) rotation = entry.RotationY;
                }
                record.HasPlacement = true;
                record.CandidateIndex = index;
                record.Name = (int)candidate.Room.Name;
                record.DuplicateId = Count(selected, candidate);
                record.PositionX = position.x; record.PositionY = position.y; record.PositionZ = position.z;
                record.RotationY = rotation;
                selected.Add((interpretation, candidate));
            }
            return plan;
        }
        finally
        {
            foreach (var pair in originals) generator.Atlases[pair.Key] = pair.Value;
            foreach (var clone in clones) if (clone != null) UnityEngine.Object.Destroy(clone);
        }
    }

    private static int SelectCandidate(AtlasZoneGenerator generator, AtlasInterpretation interpretation, Random rng,
        List<(AtlasInterpretation Interpretation, SpawnableRoom Candidate)> selected)
    {
        var candidates = new List<int>();
        float weight = 0f;
        bool specific = interpretation.SpecificRooms.Length != 0;
        for (int index = 0; index < generator.CompatibleRooms.Length; index++)
        {
            SpawnableRoom candidate = EffectiveCandidate(generator.CompatibleRooms[index]);
            int previous = Count(selected, candidate);
            if (specific != candidate.SpecialRoom ||
                (specific && !ContainsRoom(interpretation, candidate.Room.Name)) ||
                candidate.Room.Shape != interpretation.RoomShape || previous >= candidate.MaxAmount) continue;
            if (previous < candidate.MinAmount)
                return index;
            weight += Weight(selected, interpretation.Coords, candidate);
            candidates.Add(index);
        }
        if (candidates.Count == 0 || weight == 0f)
            throw new InvalidOperationException($"No room candidate for {generator.TargetZone} {interpretation.Coords}.");
        double chosen = rng.NextDouble() * weight;
        float accumulated = 0f;
        foreach (int index in candidates)
        {
            accumulated += Weight(selected, interpretation.Coords, EffectiveCandidate(generator.CompatibleRooms[index]));
            if (chosen > accumulated) continue;
            return index;
        }
        throw new InvalidOperationException("Deterministic facility candidate selection failed.");
    }

    private static int Count(List<(AtlasInterpretation Interpretation, SpawnableRoom Candidate)> selected, SpawnableRoom candidate)
    {
        int count = 0;
        foreach (var room in selected) if (room.Candidate == candidate) count++;
        return count;
    }

    private static float Weight(List<(AtlasInterpretation Interpretation, SpawnableRoom Candidate)> selected, Vector2Int coords, SpawnableRoom candidate)
    {
        float weight = candidate.ChanceMultiplier;
        foreach (var room in selected)
            if (room.Candidate == candidate && (room.Interpretation.Coords == coords + Vector2Int.up ||
                room.Interpretation.Coords == coords + Vector2Int.down || room.Interpretation.Coords == coords + Vector2Int.left ||
                room.Interpretation.Coords == coords + Vector2Int.right)) weight *= candidate.AdjacentChanceMultiplier;
        return weight;
    }
#endif

    private static SpawnableRoom EffectiveCandidate(SpawnableRoom original) =>
        original.HolidayVariants.TryGetResult<HolidayRoomVariant, SpawnableRoom>(out var variant) && variant != null ? variant : original;

    private static bool ContainsRoom(AtlasInterpretation interpretation, RoomName room)
    {
        foreach (var candidate in interpretation.SpecificRooms) if (candidate == room) return true;
        return false;
    }

    internal static void Materialize(AtlasZoneGenerator generator, FacilitySnapshotMessage snapshot)
    {
        FacilityZonePlan plan = null;
        foreach (var zone in snapshot.Zones) if (zone.ZoneId == (byte)generator.TargetZone) { plan = zone; break; }
        if (plan == null || generator.Spawned.Count != 0)
            throw new InvalidDataException("Missing or already materialized facility zone.");
        if (!EqualDigest(plan.CatalogDigest, CatalogDigest(generator)))
            throw new InvalidDataException("Facility prefab catalog changed after planning.");
        // Allocate the game's prefab identity ranges in their original zone/order,
        // including unused candidates. Never allocate ranges during the planning pass.
        foreach (var original in generator.CompatibleRooms) EffectiveCandidate(original).RegisterIdentities();
#if ANOMALY_CLIENT
        // AtlasInterpretation is a boxed IL2CPP value type. ReferenceArray's
        // value-type setter unboxes/copies its payload (unlike generic List.Add).
        var interpretations = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<AtlasInterpretation>(plan.Rooms.Count);
        var entrance = generator.TryCast<EntranceZoneGenerator>();
#else
        var interpretations = new AtlasInterpretation[plan.Rooms.Count];
        var entrance = generator as EntranceZoneGenerator;
#endif
        for (int i = 0; i < plan.Rooms.Count; i++)
        {
            var record = plan.Rooms[i];
            var specific = new RoomName[record.SpecificRooms.Count];
            for (int j = 0; j < specific.Length; j++) specific[j] = (RoomName)record.SpecificRooms[j];
            interpretations[i] = new AtlasInterpretation
            {
                Coords = new Vector2Int(record.CoordsX, record.CoordsY), RoomShape = (RoomShape)record.Shape,
                RotationY = record.InterpretationRotationY, SpecificRooms = specific,
            };
        }
        generator.Interpreted = interpretations;
        if (entrance != null) entrance._positionOffset = new Vector3(plan.EntranceOffsetX, plan.EntranceOffsetY, plan.EntranceOffsetZ);
        for (int i = 0; i < plan.Rooms.Count; i++)
        {
            var record = plan.Rooms[i];
            if (!record.HasPlacement || record.IsRemoved) continue;
            if (record.CandidateIndex < 0 || record.CandidateIndex >= generator.CompatibleRooms.Length)
                throw new InvalidDataException("Invalid facility candidate index.");
            var candidate = EffectiveCandidate(generator.CompatibleRooms[record.CandidateIndex]);
            if ((int)candidate.Room.Name != record.Name || (int)candidate.Room.Shape != record.Shape)
                throw new InvalidDataException("Facility candidate metadata did not match.");
            Spawn(generator, interpretations[i], candidate, record);
        }
        LogGenerated(generator, snapshot);
    }

    private static void Spawn(AtlasZoneGenerator generator, AtlasInterpretation interpretation, SpawnableRoom candidate, FacilityPlannedRoom room)
    {
        var position = new Vector3(room.PositionX, room.PositionY, room.PositionZ);
        var instance = UnityEngine.Object.Instantiate(candidate, position, Quaternion.Euler(0f, room.RotationY, 0f), generator.RoomSpawnParent);
#if ANOMALY_CLIENT
        FacilityStructureRotation.Register(instance);
#endif
        instance.SetupNetIdHandlers(room.DuplicateId);
        var data = new AtlasZoneGenerator.SpawnedRoomData
        {
            ChosenCandidate = candidate, Instance = instance, Interpretation = interpretation
        };
#if ANOMALY_CLIENT
        // This generated value type is a managed wrapper class. List<T>.Add's
        // generated generic marshaller treats it as a reference and passes the
        // boxed header as struct data. The class-only IList entry unboxes natively.
        generator.Spawned.System_Collections_IList_Add(data);
#else
        generator.Spawned.Add(data);
#endif
    }

    private static byte[] CatalogDigest(AtlasZoneGenerator generator)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(generator.CompatibleRooms.Length);
        foreach (var original in generator.CompatibleRooms)
        {
            var candidate = EffectiveCandidate(original);
            writer.Write(candidate.name); writer.Write((int)candidate.Room.Name); writer.Write((int)candidate.Room.Shape);
            writer.Write(Mathf.Clamp(candidate.MaxAmount, 1, 12));
            var identities = candidate.GetComponentsInChildren<NetworkIdentity>(true);
            writer.Write(identities.Length);
            foreach (var identity in identities)
            {
                var parts = new List<string>();
                for (var current = identity.transform; current != null && current != candidate.transform; current = current.parent)
                    parts.Add(current.name + "[" + current.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + "]");
                parts.Reverse();
                writer.Write(string.Join("/", parts));
            }
        }
        writer.Flush();
        using var sha = SHA256.Create();
        return sha.ComputeHash(stream.ToArray());
    }

    internal static bool EqualDigest(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        int difference = 0;
        for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
        return difference == 0;
    }

    internal static void AdjustEntrance(EntranceZoneGenerator entrance, FacilitySnapshotMessage snapshot)
    {
        if (snapshot == null || !snapshot.HasMutations) return;
        entrance._positionOffset += CheckpointOffset(entrance._hcz, snapshot) - CheckpointOffset(entrance, snapshot);
    }

    private static Vector3 CheckpointOffset(AtlasZoneGenerator generator, FacilitySnapshotMessage snapshot)
    {
        int count = 0;
        Vector3 offset = Vector3.zero;
        foreach (var interpretation in generator.Interpreted)
        {
            if (interpretation.RoomShape != RoomShape.Straight || !ContainsRoom(interpretation, RoomName.HczCheckpointToEntranceZone)) continue;
            count++;
            if (!TryGet(snapshot, generator.TargetZone, interpretation.Coords, out var entry)) continue;
            if (entry.OpType == FacilityRecipeCodec.OpSkip)
                throw new InvalidOperationException("The two HCZ/Entrance linking checkpoints cannot be removed.");
            if ((entry.DirtyMask & FacilityRecipeCodec.DirtyPosition) != 0)
                offset += new Vector3(entry.OffsetX, entry.OffsetY, entry.OffsetZ);
        }
        if (count != 2) throw new InvalidOperationException("HCZ/Entrance require exactly two linking checkpoints.");
        return offset / count;
    }

    private static bool TryGet(FacilitySnapshotMessage snapshot, FacilityZone zone, Vector2Int coords, out FacilityRecipeEntry result)
    {
        foreach (var entry in snapshot.Recipe)
            if (entry.ZoneId == (byte)zone && entry.CoordsX == coords.x && entry.CoordsY == coords.y)
            { result = entry; return true; }
        result = default;
        return false;
    }

    private static void LogGenerated(AtlasZoneGenerator generator, FacilitySnapshotMessage snapshot)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var room in generator.Spawned)
        {
            writer.Write(room.Interpretation.Coords.x); writer.Write(room.Interpretation.Coords.y);
            writer.Write((int)room.ChosenCandidate.Room.Name); writer.Write((int)room.ChosenCandidate.Room.Shape);
            var position = room.Instance.transform.position;
            writer.Write(position.x); writer.Write(position.y); writer.Write(position.z);
            writer.Write(room.Instance.transform.eulerAngles.y);
            writer.Write(room.Instance.DuplicateId);
        }
        writer.Flush();
        using var sha = SHA256.Create();
        string text = $"Facility generated: generation={snapshot.GenerationId}, zone={generator.TargetZone}, rooms={generator.Spawned.Count}, digest={Convert.ToBase64String(sha.ComputeHash(stream.ToArray()))}.";
#if ANOMALY_CLIENT
        MelonLoader.MelonLogger.Msg(text);
#else
        LabApi.Features.Console.Logger.Info(text);
#endif
    }
}
