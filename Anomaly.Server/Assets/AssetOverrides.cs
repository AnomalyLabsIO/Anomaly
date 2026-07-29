using System;
using System.Collections.Generic;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Networking.Messages;
using Interactables.Interobjects.DoorUtils;
using MapGeneration;
using Mirror;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Assets
{
    /// <summary>
    /// Server-authoritative registry of texture overrides applied to existing
    /// networked objects (players, rooms, doors, props). Overrides are replayed
    /// to new clients after their initial file transfer completes.
    /// </summary>
    public static class AssetOverrides
    {
        private readonly struct Key : IEquatable<Key>
        {
            public readonly uint NetId;
            public readonly string ComponentPath;
            public readonly ushort MaterialIndex;

            public Key(uint netId, string path, ushort matIdx)
            {
                NetId = netId;
                ComponentPath = path ?? "";
                MaterialIndex = matIdx;
            }

            public bool Equals(Key other) =>
                NetId == other.NetId && MaterialIndex == other.MaterialIndex && ComponentPath == other.ComponentPath;

            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => (int)(NetId ^ ((uint)MaterialIndex << 16) ^ (uint)(ComponentPath?.GetHashCode() ?? 0));
        }

        private static readonly Dictionary<Key, byte[]> _overrides = new Dictionary<Key, byte[]>();
        private static bool _hookedSessionCompleted;

        public static void Init()
        {
            if (!_hookedSessionCompleted)
            {
                FileTransferSession.SessionCompleted += OnSessionCompleted;
                _hookedSessionCompleted = true;
            }
        }

        public static void Disable()
        {
            if (_hookedSessionCompleted)
            {
                FileTransferSession.SessionCompleted -= OnSessionCompleted;
                _hookedSessionCompleted = false;
            }

            var netIds = new HashSet<uint>();
            foreach (var key in _overrides.Keys)
                netIds.Add(key.NetId);
            _overrides.Clear();

            foreach (uint netId in netIds)
            {
                try
                {
                    AnomalyMessaging.SendToAll(new AssetClearAllOverridesMessage { NetId = netId });
                }
                catch (Exception ex)
                {
                    Logger.Error($"AssetOverrides: Failed to broadcast cleanup for netId {netId}: {ex}");
                }
            }
        }

        // ----- Generic (wire-level) API -----

        public static void SetTexture(uint netId, string componentPath, int materialIdx, byte[] textureHash)
        {
            if (textureHash == null || textureHash.Length != 32)
                throw new ArgumentException("Texture hash must be 32 bytes.", nameof(textureHash));
            if (materialIdx < 0 || materialIdx > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(materialIdx));

            var key = new Key(netId, componentPath, (ushort)materialIdx);
            _overrides[key] = textureHash;

            AnomalyMessaging.SendToAll(new AssetOverrideTextureMessage
            {
                NetId = netId,
                ComponentPath = componentPath ?? "",
                MaterialIndex = (ushort)materialIdx,
                TextureHash = textureHash
            });
        }

        public static void ClearTexture(uint netId, string componentPath, int materialIdx)
        {
            var key = new Key(netId, componentPath, (ushort)materialIdx);
            if (!_overrides.Remove(key))
                return;

            AnomalyMessaging.SendToAll(new AssetClearOverrideMessage
            {
                NetId = netId,
                ComponentPath = componentPath ?? "",
                MaterialIndex = (ushort)materialIdx
            });
        }

        public static void ClearAll(uint netId)
        {
            var toRemove = new List<Key>();
            foreach (var kv in _overrides)
            {
                if (kv.Key.NetId == netId)
                    toRemove.Add(kv.Key);
            }
            if (toRemove.Count == 0)
                return;

            foreach (var k in toRemove)
                _overrides.Remove(k);

            AnomalyMessaging.SendToAll(new AssetClearAllOverridesMessage { NetId = netId });
        }

        // ----- Convenience wrappers (ReferenceHub, RoomIdentifier, DoorVariant) -----

        public static void SetPlayerTexture(ReferenceHub hub, string componentPath, int matIdx, byte[] hash)
        {
            if (hub?.netIdentity == null) return;
            SetTexture(hub.netIdentity.netId, componentPath, matIdx, hash);
        }

        public static void SetRoomTexture(RoomIdentifier room, string componentPath, int matIdx, byte[] hash)
        {
            if (!TryGetNetId(room, out var netId)) return;
            SetTexture(netId, componentPath, matIdx, hash);
        }

        public static void SetDoorTexture(DoorVariant door, string componentPath, int matIdx, byte[] hash)
        {
            if (door?.netIdentity == null) return;
            SetTexture(door.netIdentity.netId, componentPath, matIdx, hash);
        }

        public static void ClearPlayer(ReferenceHub hub)
        {
            if (hub?.netIdentity == null) return;
            ClearAll(hub.netIdentity.netId);
        }

        public static void ClearRoom(RoomIdentifier room)
        {
            if (!TryGetNetId(room, out var netId)) return;
            ClearAll(netId);
        }

        public static void ClearDoor(DoorVariant door)
        {
            if (door?.netIdentity == null) return;
            ClearAll(door.netIdentity.netId);
        }

        // ----- Late-join replay -----

        private static void OnSessionCompleted(ReferenceHub hub)
        {
            if (_overrides.Count == 0)
                return;

            Logger.Info($"AssetOverrides: Replaying {_overrides.Count} overrides to {hub.LoggedNameFromRefHub()}");
            foreach (var kv in _overrides)
            {
                AnomalyMessaging.SendToClient(hub, new AssetOverrideTextureMessage
                {
                    NetId = kv.Key.NetId,
                    ComponentPath = kv.Key.ComponentPath,
                    MaterialIndex = kv.Key.MaterialIndex,
                    TextureHash = kv.Value
                });
            }
        }

        private static bool TryGetNetId(RoomIdentifier room, out uint netId)
        {
            netId = 0;
            var ni = room?.GetComponent<NetworkIdentity>();
            if (ni == null) return false;
            netId = ni.netId;
            return true;
        }
    }
}
