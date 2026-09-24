using System;
using System.Collections.Generic;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Content;
using Anomaly.Server.Api.Content;
using Anomaly.Server.Networking;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Mirror;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Api.Assets
{
    /// <summary>
    /// Server-authoritative registry of texture overrides applied to existing
    /// networked objects (players, rooms, doors, props). Overrides are replayed
    /// to new clients after their exact content activation succeeds.
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

        private static readonly Dictionary<Key, AssetPackSnapshot.AssetRecord> _overrides = new Dictionary<Key, AssetPackSnapshot.AssetRecord>();
        private static bool _hookedActivated;

        internal static void Init()
        {
            if (!_hookedActivated)
            {
                ContentGrantGate.Activated += OnActivated;
                ServerEvents.RoundRestarted += OnRoundRestarted;
                _hookedActivated = true;
            }
        }

        internal static void Disable()
        {
            if (_hookedActivated)
            {
                ContentGrantGate.Activated -= OnActivated;
                ServerEvents.RoundRestarted -= OnRoundRestarted;
                _hookedActivated = false;
            }

            var netIds = new HashSet<uint>();
            foreach (var key in _overrides.Keys)
                netIds.Add(key.NetId);
            _overrides.Clear();

            foreach (uint netId in netIds)
            {
                try
                {
                    SendToActivated(new AssetClearAllOverridesMessage { NetId = netId });
                }
                catch (Exception ex)
                {
                    Logger.Error($"AssetOverrides: Failed to broadcast cleanup for netId {netId}: {ex}");
                }
            }
        }

        // ----- Generic (wire-level) API -----

        /// <summary>
        /// Applies an indexed Texture2D to an existing network object on the game thread. Invalid
        /// targets, paths, materials, assets or capacity are rejected before state changes.
        /// State is limited to 32 keys per object and 1024 total; replacing a key remains allowed.
        /// </summary>
        public static void SetTexture(uint netId, string componentPath, int materialIdx, AssetAddress textureAddress)
        {
            TextureOverridePolicy.ValidateTarget(netId, componentPath, materialIdx);
            if (!ServerAssetManager.TryGetAsset(textureAddress, out var asset) || asset.Kind != AssetKind.Texture2D)
                throw new ArgumentException("Address must identify a Texture2D in a validated pack index.", nameof(textureAddress));
            var key = new Key(netId, componentPath, (ushort)materialIdx);
            int objectCount = 0;
            foreach (Key existing in _overrides.Keys)
                if (existing.NetId == netId) objectCount++;
            if (!TextureOverridePolicy.CanStore(_overrides.ContainsKey(key), _overrides.Count, objectCount))
                throw new InvalidOperationException("Texture overrides are limited to 32 per object and 1024 in total.");
            _overrides[key] = asset;
            SendOverrideToApprovedClients(key, asset);
        }

        /// <summary>Removes one texture address and restores the clients' original texture.</summary>
        public static void ClearTexture(uint netId, string componentPath, int materialIdx)
        {
            TextureOverridePolicy.ValidateTarget(netId, componentPath, materialIdx);
            var key = new Key(netId, componentPath, (ushort)materialIdx);
            if (!_overrides.Remove(key))
                return;

            SendToActivated(new AssetClearOverrideMessage
            {
                NetId = netId,
                ComponentPath = componentPath ?? "",
                MaterialIndex = (ushort)materialIdx
            });
        }

        /// <summary>Removes all texture addresses for a network object.</summary>
        public static void ClearAll(uint netId)
        {
            if (netId == 0) throw new ArgumentOutOfRangeException(nameof(netId));
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

            SendToActivated(new AssetClearAllOverridesMessage { NetId = netId });
        }

        // Native LabAPI object wrappers.

        public static void SetPlayerTexture(Player player, string componentPath, int matIdx, AssetAddress address)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            SetTexture(player.NetworkId, componentPath, matIdx, address);
        }

        public static void SetRoomTexture(Room room, string componentPath, int matIdx, AssetAddress address)
        {
            if (!TryGetNetId(room, out var netId)) throw new ArgumentException("Room has no network identity.", nameof(room));
            SetTexture(netId, componentPath, matIdx, address);
        }

        public static void SetDoorTexture(Door door, string componentPath, int matIdx, AssetAddress address)
        {
            if (door?.Base?.netIdentity == null) throw new ArgumentException("Door has no network identity.", nameof(door));
            SetTexture(door.Base.netIdentity.netId, componentPath, matIdx, address);
        }

        public static void ClearPlayer(Player player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            ClearAll(player.NetworkId);
        }

        public static void ClearRoom(Room room)
        {
            if (!TryGetNetId(room, out var netId)) throw new ArgumentException("Room has no network identity.", nameof(room));
            ClearAll(netId);
        }

        public static void ClearDoor(Door door)
        {
            if (door?.Base?.netIdentity == null) throw new ArgumentException("Door has no network identity.", nameof(door));
            ClearAll(door.Base.netIdentity.netId);
        }

        // ----- Late-join replay -----

        private static void OnActivated(uint netId)
        {
            foreach (ReferenceHub hub in ReferenceHub.AllHubs)
                if (hub.netId == netId) { ReplayActivated(hub); break; }
        }

        private static void ReplayActivated(ReferenceHub hub)
        {
            if (_overrides.Count == 0)
                return;

            Logger.Info($"AssetOverrides: Replaying {_overrides.Count} overrides to {hub.LoggedNameFromRefHub()}");
            foreach (var kv in _overrides)
            {
                if (!FileTransferSession.IsAssetApproved(hub, kv.Value.Address, AssetKind.Texture2D, kv.Value.IndexHash))
                    continue;
                try
                {
                    AnomalyMessaging.TrySendToClient(hub, BuildMessage(kv.Key, kv.Value));
                }
                catch (Exception exception)
                {
                    Logger.Error(
                        $"AssetOverrides: Isolated replay failure for {hub.LoggedNameFromRefHub()}: {exception}");
                }
            }
        }

        private static void SendOverrideToApprovedClients(Key key, AssetPackSnapshot.AssetRecord asset)
        {
            foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            {
                try
                {
                    if (!FileTransferSession.IsAssetApproved(hub, asset.Address, AssetKind.Texture2D, asset.IndexHash))
                        continue;
                    AnomalyMessaging.TrySendToClient(hub, BuildMessage(key, asset));
                }
                catch (Exception exception)
                {
                    SafeLogRecipientFailure(hub, exception);
                }
            }
        }

        private static void SafeLogRecipientFailure(ReferenceHub hub, Exception exception)
        {
            string name;
            try { name = hub?.LoggedNameFromRefHub() ?? "<null>"; }
            catch { name = "<unavailable>"; }
            try
            {
                Logger.Error(
                    $"AssetOverrides: Isolated approved-recipient send failure for {name}: {exception}");
            }
            catch { }
        }

        private static AssetOverrideTextureMessage BuildMessage(Key key, AssetPackSnapshot.AssetRecord asset) =>
            new AssetOverrideTextureMessage
            {
                NetId = key.NetId,
                ComponentPath = key.ComponentPath,
                MaterialIndex = key.MaterialIndex,
                TextureAddress = asset.Address.Value
            };

        private static void OnRoundRestarted()
        {
            _overrides.Clear();
        }

        private static void SendToActivated(Anomaly.Shared.Networking.IAnomalyMessage message)
        {
            foreach (ReferenceHub hub in ReferenceHub.AllHubs)
                if (ContentGrantGate.GetReadiness(hub.netId) == ContentReadiness.Activated)
                    AnomalyMessaging.TrySendToClient(hub, message);
        }
        private static bool TryGetNetId(Room room, out uint netId)
        {
            netId = 0;
            var ni = room?.Base?.GetComponent<NetworkIdentity>();
            if (ni == null) return false;
            netId = ni.netId;
            return true;
        }
    }
}
