using System.Collections.Generic;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Shared.Networking.Messages;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Assets
{
    /// <summary>
    /// Tracks every server-spawned asset on this client. Handles the six
    /// spawn/modify/reload messages that mutate this set.
    /// </summary>
    public static class SpawnedObjectRegistry
    {
        private class Entry
        {
            public uint Id;
            public byte[] SourceHash;       // bundle hash or texture hash
            public string BundleAssetName;
            public GameObject GameObject;
        }

        private static readonly Dictionary<uint, Entry> _spawns = new();

        public static void Init()
        {
            AnomalyMessaging.MessageReceived += OnMessage;
        }

        public static bool TryGet(uint id, out GameObject go)
        {
            if (_spawns.TryGetValue(id, out var entry) && entry.GameObject != null)
            {
                go = entry.GameObject;
                return true;
            }
            go = null;
            return false;
        }

        public static IEnumerable<(uint Id, GameObject Go)> All
        {
            get
            {
                foreach (var kv in _spawns)
                    if (kv.Value.GameObject != null)
                        yield return (kv.Key, kv.Value.GameObject);
            }
        }

        /// <summary>
        /// Destroys every spawn on this client. Called on disconnect.
        /// </summary>
        public static void Clear()
        {
            foreach (var entry in _spawns.Values)
            {
                if (entry.GameObject != null)
                    UnityEngine.Object.Destroy(entry.GameObject);
            }
            _spawns.Clear();
        }

        private static void OnMessage(Shared.Networking.IAnomalyMessage msg)
        {
            switch (msg)
            {
                case AssetSpawnMessage spawn: HandleSpawn(spawn); break;
                case AssetDespawnMessage despawn: HandleDespawn(despawn); break;
                case AssetSetTransformMessage xform: HandleTransform(xform); break;
                case AssetSetTextureMessage tex: HandleTexture(tex); break;
                case AssetSetActiveMessage act: HandleActive(act); break;
                case AssetReloadMessage reload: HandleReload(reload); break;
            }
        }

        private static void HandleSpawn(AssetSpawnMessage msg)
        {
            if (_spawns.ContainsKey(msg.ObjectId))
            {
                MelonLogger.Warning($"SpawnedObjectRegistry: duplicate spawn id {msg.ObjectId}, ignoring.");
                return;
            }

            if (!AnomalyResources.TryLoadFromBundle<GameObject>(msg.AssetHash, msg.BundleAssetName, out var prefab))
            {
                MelonLogger.Warning($"SpawnedObjectRegistry: failed to load '{msg.BundleAssetName}' from bundle for spawn {msg.ObjectId}.");
                return;
            }

            var go = UnityEngine.Object.Instantiate(prefab);
            go.name = $"Anomaly:{msg.ObjectId}:{msg.BundleAssetName}";

            ScriptStripper.Strip(go);
            ShaderRemapper.Remap(go);

            var t = go.transform;
            t.localPosition = new Vector3(msg.PositionX, msg.PositionY, msg.PositionZ);
            t.localRotation = new Quaternion(msg.RotationX, msg.RotationY, msg.RotationZ, msg.RotationW);
            t.localScale    = new Vector3(msg.ScaleX, msg.ScaleY, msg.ScaleZ);

            _spawns[msg.ObjectId] = new Entry
            {
                Id = msg.ObjectId,
                SourceHash = msg.AssetHash,
                BundleAssetName = msg.BundleAssetName,
                GameObject = go
            };

            AssetEvents.OnSpawned(new AssetSpawnedEventArgs(msg.ObjectId, msg.AssetHash, go));
        }

        private static void HandleDespawn(AssetDespawnMessage msg)
        {
            if (!_spawns.TryGetValue(msg.ObjectId, out var entry))
                return;

            if (entry.GameObject != null)
                UnityEngine.Object.Destroy(entry.GameObject);
            _spawns.Remove(msg.ObjectId);

            AssetEvents.OnDespawned(new AssetDespawnedEventArgs(msg.ObjectId));
        }

        private static void HandleTransform(AssetSetTransformMessage msg)
        {
            if (!_spawns.TryGetValue(msg.ObjectId, out var entry) || entry.GameObject == null)
                return;

            var t = entry.GameObject.transform;
            if ((msg.DirtyMask & AssetSetTransformMessage.DirtyPosition) != 0)
                t.localPosition = new Vector3(msg.PositionX, msg.PositionY, msg.PositionZ);
            if ((msg.DirtyMask & AssetSetTransformMessage.DirtyRotation) != 0)
                t.localRotation = new Quaternion(msg.RotationX, msg.RotationY, msg.RotationZ, msg.RotationW);
            if ((msg.DirtyMask & AssetSetTransformMessage.DirtyScale) != 0)
                t.localScale = new Vector3(msg.ScaleX, msg.ScaleY, msg.ScaleZ);
        }

        private static void HandleTexture(AssetSetTextureMessage msg)
        {
            if (!_spawns.TryGetValue(msg.ObjectId, out var entry) || entry.GameObject == null)
                return;

            var renderers = entry.GameObject.GetComponentsInChildren<Renderer>(true);
            if (msg.RendererIndex >= renderers.Length)
            {
                MelonLogger.Warning($"SpawnedObjectRegistry: renderer index {msg.RendererIndex} out of range on spawn {msg.ObjectId}.");
                return;
            }

            var renderer = renderers[msg.RendererIndex];
            var mats = renderer.materials; // note: .materials clones, which is what we want here
            if (msg.MaterialIndex >= mats.Length)
            {
                MelonLogger.Warning($"SpawnedObjectRegistry: material index {msg.MaterialIndex} out of range on renderer {msg.RendererIndex} of spawn {msg.ObjectId}.");
                return;
            }

            if (!AnomalyResources.TryGetTexture(msg.TextureHash, out var tex))
            {
                MelonLogger.Warning($"SpawnedObjectRegistry: texture not available for SetTexture on spawn {msg.ObjectId}.");
                return;
            }

            mats[msg.MaterialIndex].mainTexture = tex;
            renderer.materials = mats;
        }

        private static void HandleActive(AssetSetActiveMessage msg)
        {
            if (!_spawns.TryGetValue(msg.ObjectId, out var entry) || entry.GameObject == null)
                return;
            entry.GameObject.SetActive(msg.Active);
        }

        private static void HandleReload(AssetReloadMessage msg)
        {
            int affected = 0;
            foreach (var entry in _spawns.Values)
            {
                if (entry.SourceHash == null || entry.SourceHash.Length != 32) continue;
                if (!HashesEqual(entry.SourceHash, msg.OldHash)) continue;

                // v1: bundle reloads can't hot-swap underlying geometry without
                // destroying live spawns. Log and let the server drive despawn/respawn.
                MelonLogger.Warning(
                    $"SpawnedObjectRegistry: bundle hot-reload not supported for spawn {entry.Id}; despawn/respawn required.");
                entry.SourceHash = msg.NewHash;
                affected++;
            }

            // Drop the cached Unity object for the old hash so future loads rebuild it.
            AnomalyResources.UnloadOne(msg.OldHash);

            // The OverrideRegistry is the other consumer of reloads (textures on
            // existing networked objects); it fires its own event path. We report
            // spawn-side counts here only.
            AssetEvents.OnReloaded(new AssetReloadedEventArgs(msg.OldHash, msg.NewHash, affected, 0));
        }

        private static bool HashesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
