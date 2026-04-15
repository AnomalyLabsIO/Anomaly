using System;
using System.Collections.Generic;
using System.Threading;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Networking.Messages;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Assets
{
    /// <summary>
    /// Server-authoritative spawn registry. Plugins call <see cref="Spawn"/> to
    /// instantiate a custom asset on all connected clients; the resulting
    /// <see cref="uint"/> ID is the handle for all subsequent mutation calls
    /// (<see cref="SetTransform"/>, <see cref="SetTexture"/>, <see cref="SetActive"/>,
    /// <see cref="Despawn"/>).
    /// </summary>
    public static class AssetSpawner
    {
        private class SpawnedAsset
        {
            public uint Id;
            public byte[] AssetHash;
            public string BundleAssetName;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public bool Active = true;
        }

        private static readonly Dictionary<uint, SpawnedAsset> _spawns = new Dictionary<uint, SpawnedAsset>();
        private static int _nextId; // Interlocked-incremented; first valid id is 1.
        private static bool _hookedSessionCompleted;

        public static void Init()
        {
            if (!_hookedSessionCompleted)
            {
                FileTransferSession.SessionCompleted += OnSessionCompleted;
                _hookedSessionCompleted = true;
            }
        }

        /// <summary>
        /// Broadcasts a spawn for the given asset hash to all connected clients
        /// and records the spawn for late-join replay. Returns the server-assigned
        /// object id.
        /// </summary>
        public static uint Spawn(byte[] hash, string bundleAssetName = "",
            Vector3 pos = default, Quaternion rot = default, Vector3 scale = default)
        {
            if (hash == null || hash.Length != 32)
                throw new ArgumentException("Asset hash must be 32 bytes.", nameof(hash));
            if (scale == default) scale = Vector3.one;
            if (rot == default) rot = Quaternion.identity;

            uint id = (uint)Interlocked.Increment(ref _nextId);
            var spawn = new SpawnedAsset
            {
                Id = id,
                AssetHash = hash,
                BundleAssetName = bundleAssetName ?? "",
                Position = pos,
                Rotation = rot,
                Scale = scale,
                Active = true
            };
            _spawns[id] = spawn;

            AnomalyMessaging.SendToAll(BuildSpawnMessage(spawn));
            return id;
        }

        public static void Despawn(uint id)
        {
            if (!_spawns.Remove(id))
                return;
            AnomalyMessaging.SendToAll(new AssetDespawnMessage { ObjectId = id });
        }

        public static void SetTransform(uint id, Vector3? pos = null, Quaternion? rot = null, Vector3? scale = null)
        {
            if (!_spawns.TryGetValue(id, out var spawn))
                return;

            byte dirty = 0;
            var msg = new AssetSetTransformMessage { ObjectId = id };

            if (pos.HasValue)
            {
                spawn.Position = pos.Value;
                dirty |= AssetSetTransformMessage.DirtyPosition;
                msg.PositionX = pos.Value.x; msg.PositionY = pos.Value.y; msg.PositionZ = pos.Value.z;
            }
            if (rot.HasValue)
            {
                spawn.Rotation = rot.Value;
                dirty |= AssetSetTransformMessage.DirtyRotation;
                msg.RotationX = rot.Value.x; msg.RotationY = rot.Value.y; msg.RotationZ = rot.Value.z; msg.RotationW = rot.Value.w;
            }
            if (scale.HasValue)
            {
                spawn.Scale = scale.Value;
                dirty |= AssetSetTransformMessage.DirtyScale;
                msg.ScaleX = scale.Value.x; msg.ScaleY = scale.Value.y; msg.ScaleZ = scale.Value.z;
            }

            if (dirty == 0) return;
            msg.DirtyMask = dirty;
            AnomalyMessaging.SendToAll(msg);
        }

        public static void SetTexture(uint id, int rendererIdx, int materialIdx, byte[] textureHash)
        {
            if (textureHash == null || textureHash.Length != 32)
                throw new ArgumentException("Texture hash must be 32 bytes.", nameof(textureHash));
            if (!_spawns.ContainsKey(id))
                return;

            AnomalyMessaging.SendToAll(new AssetSetTextureMessage
            {
                ObjectId = id,
                RendererIndex = (ushort)rendererIdx,
                MaterialIndex = (ushort)materialIdx,
                TextureHash = textureHash
            });
        }

        public static void SetActive(uint id, bool active)
        {
            if (!_spawns.TryGetValue(id, out var spawn))
                return;
            spawn.Active = active;
            AnomalyMessaging.SendToAll(new AssetSetActiveMessage { ObjectId = id, Active = active });
        }

        /// <summary>
        /// Replay every active spawn to a specific hub after they finish their
        /// initial file transfer. Called automatically via
        /// <see cref="FileTransferSession.SessionCompleted"/>.
        /// </summary>
        private static void OnSessionCompleted(ReferenceHub hub)
        {
            if (_spawns.Count == 0)
                return;

            Logger.Info($"AssetSpawner: Replaying {_spawns.Count} active spawns to {hub.LoggedNameFromRefHub()}");
            foreach (var spawn in _spawns.Values)
            {
                AnomalyMessaging.SendToClient(hub, BuildSpawnMessage(spawn));
                if (!spawn.Active)
                {
                    AnomalyMessaging.SendToClient(hub,
                        new AssetSetActiveMessage { ObjectId = spawn.Id, Active = false });
                }
            }
        }

        private static AssetSpawnMessage BuildSpawnMessage(SpawnedAsset s) => new AssetSpawnMessage
        {
            ObjectId = s.Id,
            AssetHash = s.AssetHash,
            BundleAssetName = s.BundleAssetName,
            PositionX = s.Position.x, PositionY = s.Position.y, PositionZ = s.Position.z,
            RotationX = s.Rotation.x, RotationY = s.Rotation.y, RotationZ = s.Rotation.z, RotationW = s.Rotation.w,
            ScaleX = s.Scale.x, ScaleY = s.Scale.y, ScaleZ = s.Scale.z
        };
    }
}
