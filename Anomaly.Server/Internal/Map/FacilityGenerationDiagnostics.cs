using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
#if ANOMALY_CLIENT
using System.Text.Json;
#else
using System.Runtime.Serialization.Json;
using System.Text;
#endif
using UnityEngine;
#if ANOMALY_CLIENT
using Il2CppMapGeneration;
namespace Anomaly.Client.Internal.Map;
#else
using MapGeneration;
namespace Anomaly.Server.Internal.Map;
#endif

/// <summary>Detached generation-time evidence for the local qualification harness.</summary>
internal static class FacilityGenerationDiagnostics
{
    private static string _capture;

    internal static string Capture() => _capture;
    internal static void Clear() => _capture = null;

    internal static byte[] ComputeRoomsDigest() => Digest(ReadRooms());

    private static List<RoomDescriptor> ReadRooms()
    {
        var rooms = new List<RoomDescriptor>();
        foreach (var generator in UnityEngine.Object.FindObjectsByType<AtlasZoneGenerator>(FindObjectsSortMode.None))
        {
            foreach (var room in generator.Spawned)
            {
                var instance = room.Instance;
                var identities = new List<IdentityDescriptor>();
                int identityCount = instance.OriginalIdentities.Length;
                for (int i = 0; i < identityCount; i++)
                {
                    var identity = instance.OriginalIdentities[i];
                    // Registration reparents some children into a shared scene
                    // container. Its sibling indices are not room identity.
                    var original = room.ChosenCandidate.OriginalIdentities[i];
                    identities.Add(new IdentityDescriptor
                    {
                        Path = RelativePath(original.Target.transform, room.ChosenCandidate.transform),
                        OriginalAssetId = identity.AssetId,
                        AssetId = identity.AssetId + (uint)(identityCount * instance.DuplicateId),
                    });
                }
                identities.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
                var position = instance.transform.position;
                var rotation = instance.transform.rotation;
                rooms.Add(new RoomDescriptor
                {
                    Zone = (int)generator.TargetZone,
                    X = room.Interpretation.Coords.x,
                    Y = room.Interpretation.Coords.y,
                    Candidate = (int)room.ChosenCandidate.Room.Name,
                    Shape = (int)room.ChosenCandidate.Room.Shape,
                    Position = new[] { position.x, position.y, position.z },
                    Rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w },
                    DuplicateId = instance.DuplicateId,
                    Identities = identities,
                });
            }
        }
        rooms.Sort((left, right) =>
        {
            int result = left.Zone.CompareTo(right.Zone);
            if (result == 0) result = left.X.CompareTo(right.X);
            if (result == 0) result = left.Y.CompareTo(right.Y);
            return result;
        });
        return rooms;
    }

    private static byte[] Digest(List<RoomDescriptor> rooms)
    {
        // JSON implementations on Mono and .NET format float32 values differently.
        // Hash an explicit little-endian binary representation, not JSON text.
        using var evidence = new MemoryStream();
        using var writer = new BinaryWriter(evidence);
        writer.Write(2); // Prefab ownership paths survive native child reparenting.
        writer.Write(rooms.Count);
        foreach (var room in rooms)
        {
            writer.Write(room.Zone); writer.Write(room.X); writer.Write(room.Y);
            writer.Write(room.Candidate); writer.Write(room.Shape);
            foreach (float value in room.Position) writer.Write(value);
            foreach (float value in room.Rotation) writer.Write(value);
            writer.Write(room.DuplicateId);
            writer.Write(room.Identities.Count);
            foreach (var identity in room.Identities)
            {
                writer.Write(identity.Path); writer.Write(identity.OriginalAssetId); writer.Write(identity.AssetId);
            }
        }
        writer.Flush();
        using var sha = SHA256.Create();
        return sha.ComputeHash(evidence.ToArray());
    }

    internal static void Record()
    {
#if ANOMALY_CLIENT
        var snapshot = FacilityRecipeClient.CurrentSnapshot;
#else
        var snapshot = FacilityLayoutRuntime.CurrentSnapshot;
#endif
        if (snapshot == null || !SeedSynchronizer.MapGenerated) return;
        var rooms = ReadRooms();
        string roomsDigest = Convert.ToBase64String(Digest(rooms));
        var capture = new FacilityCapture
        {
            GenerationId = snapshot.GenerationId,
            Seed = snapshot.Seed,
            SnapshotDigest = Convert.ToBase64String(snapshot.Digest),
            RoomsDigest = roomsDigest,
            Rooms = rooms,
        };
#if ANOMALY_CLIENT
        _capture = JsonSerializer.Serialize(capture);
#else
        using var json = new MemoryStream();
        new DataContractJsonSerializer(typeof(FacilityCapture)).WriteObject(json, capture);
        _capture = Encoding.UTF8.GetString(json.ToArray());
#endif
        string message = $"Facility generation evidence: generation={snapshot.GenerationId}, seed={snapshot.Seed}, rooms={rooms.Count}, digest={roomsDigest}.";
#if ANOMALY_CLIENT
        MelonLoader.MelonLogger.Msg(message);
#else
        LabApi.Features.Console.Logger.Info(message);
#endif
    }

    private static string RelativePath(Transform target, Transform room)
    {
        var parts = new List<string>();
        for (var current = target; current != null && current != room; current = current.parent)
            parts.Add(current.name + "[" + current.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + "]");
        parts.Reverse();
        return string.Join("/", parts);
    }

    [DataContract]
    private sealed class FacilityCapture
    {
        [DataMember(Name = "GenerationId", Order = 0)] public ulong GenerationId { get; set; }
        [DataMember(Name = "Seed", Order = 1)] public int Seed { get; set; }
        [DataMember(Name = "SnapshotDigest", Order = 2)] public string SnapshotDigest { get; set; }
        [DataMember(Name = "RoomsDigest", Order = 3)] public string RoomsDigest { get; set; }
        [DataMember(Name = "Rooms", Order = 4)] public List<RoomDescriptor> Rooms { get; set; }
    }

    [DataContract]
    private sealed class RoomDescriptor
    {
        [DataMember(Name = "Zone", Order = 0)] public int Zone { get; set; }
        [DataMember(Name = "X", Order = 1)] public int X { get; set; }
        [DataMember(Name = "Y", Order = 2)] public int Y { get; set; }
        [DataMember(Name = "Candidate", Order = 3)] public int Candidate { get; set; }
        [DataMember(Name = "Shape", Order = 4)] public int Shape { get; set; }
        [DataMember(Name = "Position", Order = 5)] public float[] Position { get; set; }
        [DataMember(Name = "Rotation", Order = 6)] public float[] Rotation { get; set; }
        [DataMember(Name = "DuplicateId", Order = 7)] public int DuplicateId { get; set; }
        [DataMember(Name = "Identities", Order = 8)] public List<IdentityDescriptor> Identities { get; set; }
    }

    [DataContract]
    private sealed class IdentityDescriptor
    {
        [DataMember(Name = "Path", Order = 0)] public string Path { get; set; }
        [DataMember(Name = "OriginalAssetId", Order = 1)] public uint OriginalAssetId { get; set; }
        [DataMember(Name = "AssetId", Order = 2)] public uint AssetId { get; set; }
    }
}
