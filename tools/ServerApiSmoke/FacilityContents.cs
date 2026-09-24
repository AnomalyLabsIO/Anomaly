using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using UnityEngine;
#if FACILITY_CLIENT_SMOKE
using Il2CppMapGeneration;
using Il2CppMapGeneration.Distributors;
using Il2CppMapGeneration.RoomConnectors;
using Il2CppInteractables.Interobjects.DoorUtils;
using NetworkIdentity = Il2CppMirror.NetworkIdentity;
using NetworkRegistry = Il2CppMirror.NetworkClient;
using LczLabel = Il2Cpp.LCZ_Label;
#else
using MapGeneration;
using MapGeneration.Distributors;
using MapGeneration.RoomConnectors;
using Interactables.Interobjects.DoorUtils;
using NetworkIdentity = Mirror.NetworkIdentity;
using NetworkRegistry = Mirror.NetworkServer;
using LczLabel = LCZ_Label;
#endif

namespace Anomaly.ServerApiSmoke;

/// <summary>Smoke-only, post-ready observations of room-owned network objects and atlas adjacency.</summary>
/// <remarks>
/// Capture on the game thread after initial spawn delivery. Active flags and current hierarchy
/// paths are diagnostic only: client culling and server/client startup may differ. This does not
/// assert renderer, collider, animated child, or non-atlas room parity.
/// </remarks>
internal static class FacilityContents
{
    internal static string Capture()
    {
        var rooms = new List<RoomCapture>();
        var roomKeys = new Dictionary<int, string>();
        var roomIdentifiers = new List<RoomIdentifier>();
        foreach (var generator in UnityEngine.Object.FindObjectsByType<AtlasZoneGenerator>(FindObjectsSortMode.None))
        {
            foreach (var spawned in generator.Spawned)
            {
                var instance = spawned.Instance;
                if (instance == null) throw new InvalidOperationException("A generated room instance was destroyed before content capture.");
                int zone = (int)generator.TargetZone;
                int x = spawned.Interpretation.Coords.x;
                int y = spawned.Interpretation.Coords.y;
                string key = zone.ToString(CultureInfo.InvariantCulture) + ":" +
                    x.ToString(CultureInfo.InvariantCulture) + ":" + y.ToString(CultureInfo.InvariantCulture);
                var room = new RoomCapture
                {
                    Key = key, Zone = zone, X = x, Y = y,
                    Name = (int)spawned.ChosenCandidate.Room.Name,
                    Shape = (int)spawned.ChosenCandidate.Room.Shape,
                    DuplicateId = instance.DuplicateId,
                    Position = Position(instance.transform), Rotation = Rotation(instance.transform),
                };
                var originals = instance.OriginalIdentities;
                var prefabOriginals = spawned.ChosenCandidate.OriginalIdentities;
                for (int index = 0; index < originals.Length; index++)
                {
                    var original = originals[index];
                    var target = original.Target;
                    var prefabTarget = prefabOriginals != null && index < prefabOriginals.Length
                        ? prefabOriginals[index].Target : null;
                    bool present = target != null;
                    var identity = present ? target.GetComponent<NetworkIdentity>() : null;
                    var structureSync = present ? target.GetComponent<StructurePositionSync>() : null;
                    bool hasIdentity = identity != null;
                    bool registered = hasIdentity && identity.netId != 0 &&
                        NetworkRegistry.spawned.TryGetValue(identity.netId, out var current) &&
                        current != null && current.GetInstanceID() == identity.GetInstanceID();
                    string currentPath = present ? RelativePath(target.transform, instance.transform) : null;
                    room.Identities.Add(new IdentityCapture
                    {
                        Index = index,
                        Path = prefabTarget != null ? RelativePath(prefabTarget.transform, spawned.ChosenCandidate.transform) : currentPath,
                        PathFromPrefab = prefabTarget != null,
                        OriginalAssetId = original.AssetId,
                        AssetId = original.AssetId + (uint)(originals.Length * instance.DuplicateId),
                        ActualAssetId = hasIdentity ? identity.assetId : 0,
                        NetId = hasIdentity ? identity.netId : 0,
                        Present = present, HasNetworkIdentity = hasIdentity, Spawned = registered,
                        HasStructurePositionSync = structureSync != null,
                        StructureYawCode = structureSync != null ? structureSync.Network_rotationY : 0,
                        ActiveSelf = present && target.activeSelf,
                        ActiveInHierarchy = present && target.activeInHierarchy,
                        RoomParented = present && target.transform.IsChildOf(instance.transform),
                        CurrentPath = currentPath,
                        Position = present ? Position(target.transform) : null,
                        Rotation = present ? Rotation(target.transform) : null,
                    });
                }
                room.Identities.Sort((left, right) => left.Index.CompareTo(right.Index));
                foreach (var label in instance.GetComponentsInChildren<LczLabel>(true))
                    room.Labels.Add(new LabelCapture
                    {
                        Path = RelativePath(label.transform, instance.transform),
                        CharacterRendererPresent = label.chRend != null,
                        NumberRendererPresent = label.numRend != null,
                        CharacterMaterial = label.chRend != null && label.chRend.sharedMaterial != null ? label.chRend.sharedMaterial.name : null,
                        NumberMaterial = label.numRend != null && label.numRend.sharedMaterial != null ? label.numRend.sharedMaterial.name : null,
                    });
                room.Labels.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
                rooms.Add(room);
                roomKeys.Add(instance.Room.GetInstanceID(), key);
                roomIdentifiers.Add(instance.Room);
            }
        }
        rooms.Sort((left, right) =>
        {
            int result = left.Zone.CompareTo(right.Zone);
            if (result == 0) result = left.X.CompareTo(right.X);
            return result == 0 ? left.Y.CompareTo(right.Y) : result;
        });

        var edges = new List<EdgeCapture>();
        var edgeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var room in roomIdentifiers)
        {
            string from = roomKeys[room.GetInstanceID()];
            foreach (var connected in room.ConnectedRooms)
            {
                if (connected == null || !roomKeys.TryGetValue(connected.GetInstanceID(), out var to)) continue;
                if (edgeKeys.Add(from + ">" + to)) edges.Add(new EdgeCapture { From = from, To = to });
            }
        }
        edges.Sort((left, right) =>
        {
            int result = string.CompareOrdinal(left.From, right.From);
            return result == 0 ? string.CompareOrdinal(left.To, right.To) : result;
        });

        // ConnectedRooms is a generation-time cache. Observe the actual server-spawned
        // connectors separately, after their network identities have reached the client.
        var connectors = new List<ConnectorCapture>();
        foreach (var connector in UnityEngine.Object.FindObjectsByType<SpawnableRoomConnector>(FindObjectsSortMode.None))
        {
            var identity = connector.GetComponent<NetworkIdentity>();
            var door = connector.GetComponent<DoorVariant>();
            var doorRooms = new List<string>();
            if (door != null && door.Rooms != null)
                foreach (var room in door.Rooms)
                    if (room != null && roomKeys.TryGetValue(room.GetInstanceID(), out var key) && !doorRooms.Contains(key))
                        doorRooms.Add(key);
            doorRooms.Sort(StringComparer.Ordinal);
            connectors.Add(new ConnectorCapture
            {
                Name = connector.name,
                ConnectorType = (int)connector.SpawnData.ConnectorType,
                HasNetworkIdentity = identity != null,
                NetId = identity != null ? identity.netId : 0,
                AssetId = identity != null ? identity.assetId : 0,
                Spawned = identity != null && identity.netId != 0 &&
                    NetworkRegistry.spawned.TryGetValue(identity.netId, out var current) &&
                    current != null && current.GetInstanceID() == identity.GetInstanceID(),
                Position = Position(connector.transform), Rotation = Rotation(connector.transform),
                HasDoor = door != null,
                DoorRoomsRegistered = door != null && door.RoomsAlreadyRegistered,
                DoorRooms = doorRooms,
            });
        }
        connectors.Sort((left, right) => left.NetId.CompareTo(right.NetId));

        var assetIds = new HashSet<uint>();
        foreach (var identity in NetworkRegistry.spawned.Values)
            if (identity != null && identity.assetId != 0) assetIds.Add(identity.assetId);
        var sortedAssetIds = new List<uint>(assetIds);
        sortedAssetIds.Sort();
        int identityCount = 0;
        foreach (var room in rooms) identityCount += room.Identities.Count;
        return JsonSerializer.Serialize(new ContentsCapture
        {
            SchemaVersion = 2, Seed = SeedSynchronizer.Seed, MapGenerated = SeedSynchronizer.MapGenerated,
            RoomCount = rooms.Count, IdentityCount = identityCount,
            ConnectorCount = connectors.Count, Connectors = connectors,
            Rooms = rooms, Edges = edges, SpawnedAssetIds = sortedAssetIds,
        });
    }

    private static float[] Position(Transform transform)
    {
        var position = transform.position;
        return new[] { position.x, position.y, position.z };
    }

    private static float[] Rotation(Transform transform)
    {
        var rotation = transform.rotation;
        return new[] { rotation.x, rotation.y, rotation.z, rotation.w };
    }

    private static string RelativePath(Transform target, Transform room)
    {
        var parts = new List<string>();
        for (var current = target; current != null && current != room; current = current.parent)
            parts.Add(current.name + "[" + current.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + "]");
        parts.Reverse();
        return string.Join("/", parts);
    }

    private sealed class ContentsCapture
    {
        public int SchemaVersion { get; set; }
        public int Seed { get; set; }
        public bool MapGenerated { get; set; }
        public int RoomCount { get; set; }
        public int IdentityCount { get; set; }
        public int ConnectorCount { get; set; }
        public List<ConnectorCapture> Connectors { get; set; }
        public List<RoomCapture> Rooms { get; set; }
        public List<EdgeCapture> Edges { get; set; }
        public List<uint> SpawnedAssetIds { get; set; }
    }

    private sealed class RoomCapture
    {
        public string Key { get; set; }
        public int Zone { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Name { get; set; }
        public int Shape { get; set; }
        public int DuplicateId { get; set; }
        public float[] Position { get; set; }
        public float[] Rotation { get; set; }
        public List<IdentityCapture> Identities { get; } = new List<IdentityCapture>();
        public List<LabelCapture> Labels { get; } = new List<LabelCapture>();
    }

    private sealed class IdentityCapture
    {
        public int Index { get; set; }
        public string Path { get; set; }
        public bool PathFromPrefab { get; set; }
        public uint OriginalAssetId { get; set; }
        public uint AssetId { get; set; }
        public uint ActualAssetId { get; set; }
        public uint NetId { get; set; }
        public bool Present { get; set; }
        public bool HasNetworkIdentity { get; set; }
        public bool Spawned { get; set; }
        public bool HasStructurePositionSync { get; set; }
        public int StructureYawCode { get; set; }
        public bool ActiveSelf { get; set; }
        public bool ActiveInHierarchy { get; set; }
        public bool RoomParented { get; set; }
        public string CurrentPath { get; set; }
        public float[] Position { get; set; }
        public float[] Rotation { get; set; }
    }

    private sealed class EdgeCapture
    {
        public string From { get; set; }
        public string To { get; set; }
    }

    private sealed class LabelCapture
    {
        public string Path { get; set; }
        public bool CharacterRendererPresent { get; set; }
        public bool NumberRendererPresent { get; set; }
        public string CharacterMaterial { get; set; }
        public string NumberMaterial { get; set; }
    }

    private sealed class ConnectorCapture
    {
        public string Name { get; set; }
        public int ConnectorType { get; set; }
        public bool HasNetworkIdentity { get; set; }
        public uint NetId { get; set; }
        public uint AssetId { get; set; }
        public bool Spawned { get; set; }
        public float[] Position { get; set; }
        public float[] Rotation { get; set; }
        public bool HasDoor { get; set; }
        public bool DoorRoomsRegistered { get; set; }
        public List<string> DoorRooms { get; set; }
    }
}
