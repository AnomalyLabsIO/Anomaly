using System;
using System.Collections.Generic;
using Anomaly.Client.API.Assets;
using Anomaly.Client.API.Networking;
using Anomaly.Client.Events.Args;
using Anomaly.Client.Events.Handlers;
using Anomaly.Shared.Networking.Messages;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Assets
{
    /// <summary>
    /// Client-side registry of texture overrides applied to existing
    /// <c>NetworkIdentity</c> objects (player skins, room walls, doors, props).
    /// Handles the three override messages plus retry logic for targets that
    /// weren't resolvable at the moment the override arrived.
    /// </summary>
    public static class OverrideRegistry
    {
        private readonly struct Key : IEquatable<Key>
        {
            public readonly uint NetId;
            public readonly string ComponentPath;
            public readonly int MaterialIndex;

            public Key(uint netId, string path, int matIdx)
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

        private class Entry
        {
            public byte[] TextureHash;
            public Material AppliedMaterial;
            public Texture OriginalTexture;
            public bool Applied;
        }

        private static readonly Dictionary<Key, Entry> _overrides = new();

        public static void Init()
        {
            AnomalyMessaging.MessageReceived += OnMessage;

            // After the initial file transfer completes any pending overrides that
            // were waiting on a not-yet-cached texture hash might now be resolvable.
            FileTransferEvents.Complete += _ => ReapplyAll();
        }

        /// <summary>
        /// Re-resolve and re-apply all overrides. Call this after events that
        /// replace the GameObject hierarchy under a stable <c>netId</c>, e.g. a
        /// player role change. Safe to call redundantly.
        /// </summary>
        public static void ReapplyAll()
        {
            foreach (var kv in _overrides)
                Apply(kv.Key, kv.Value);
        }

        /// <summary>
        /// Re-resolve and re-apply every override keyed to a specific NetId.
        /// Called from <c>NetworkIdentity.OnStartClient</c> patches when a new
        /// object spawns, and safe to call from mod code after known role swaps.
        /// </summary>
        public static void ReapplyForNetId(uint netId)
        {
            foreach (var kv in _overrides)
            {
                if (kv.Key.NetId == netId)
                    Apply(kv.Key, kv.Value);
            }
        }

        /// <summary>
        /// Restores every override back to its cached original. Called on
        /// disconnect so reconnecting to a vanilla server doesn't leave stale
        /// custom textures bleeding through.
        /// </summary>
        public static void Clear()
        {
            foreach (var kv in _overrides)
            {
                if (kv.Value.Applied && kv.Value.AppliedMaterial != null)
                    kv.Value.AppliedMaterial.mainTexture = kv.Value.OriginalTexture;
            }
            _overrides.Clear();
        }

        private static void OnMessage(Shared.Networking.IAnomalyMessage msg)
        {
            switch (msg)
            {
                case AssetOverrideTextureMessage ot: HandleOverride(ot); break;
                case AssetClearOverrideMessage co: HandleClearOne(co); break;
                case AssetClearAllOverridesMessage cao: HandleClearAll(cao); break;
                case AssetReloadMessage rl: HandleReload(rl); break;
            }
        }

        private static void HandleOverride(AssetOverrideTextureMessage msg)
        {
            var key = new Key(msg.NetId, msg.ComponentPath, msg.MaterialIndex);

            // If one was applied previously, revert first so we don't lose the
            // "original before any override" reference.
            if (_overrides.TryGetValue(key, out var existing) && existing.Applied && existing.AppliedMaterial != null)
            {
                existing.AppliedMaterial.mainTexture = existing.OriginalTexture;
            }

            var entry = new Entry { TextureHash = msg.TextureHash };
            _overrides[key] = entry;
            Apply(key, entry);
        }

        private static void HandleClearOne(AssetClearOverrideMessage msg)
        {
            var key = new Key(msg.NetId, msg.ComponentPath, msg.MaterialIndex);
            if (!_overrides.TryGetValue(key, out var entry))
                return;

            if (entry.Applied && entry.AppliedMaterial != null)
                entry.AppliedMaterial.mainTexture = entry.OriginalTexture;

            _overrides.Remove(key);
            AssetEvents.OnOverrideCleared(new AssetOverrideClearedEventArgs(msg.NetId, msg.ComponentPath, msg.MaterialIndex));
        }

        private static void HandleClearAll(AssetClearAllOverridesMessage msg)
        {
            var toRemove = new List<Key>();
            foreach (var kv in _overrides)
            {
                if (kv.Key.NetId != msg.NetId) continue;
                if (kv.Value.Applied && kv.Value.AppliedMaterial != null)
                    kv.Value.AppliedMaterial.mainTexture = kv.Value.OriginalTexture;
                toRemove.Add(kv.Key);
            }
            foreach (var k in toRemove)
            {
                _overrides.Remove(k);
                AssetEvents.OnOverrideCleared(new AssetOverrideClearedEventArgs(k.NetId, k.ComponentPath, k.MaterialIndex));
            }
        }

        private static void HandleReload(AssetReloadMessage msg)
        {
            int affected = 0;
            foreach (var kv in _overrides)
            {
                if (kv.Value.TextureHash == null) continue;
                if (!HashesEqual(kv.Value.TextureHash, msg.OldHash)) continue;

                kv.Value.TextureHash = msg.NewHash;
                // Force re-resolution & re-apply so the new bytes replace the old in-place.
                AnomalyResources.UnloadOne(msg.OldHash);
                Apply(kv.Key, kv.Value);
                affected++;
            }
            // Spawn-side registry reports its own counts via its own OnReloaded;
            // we don't fire again here.
            if (affected > 0)
                MelonLogger.Msg($"OverrideRegistry: swapped {affected} override(s) to new hash.");
        }

        private static void Apply(Key key, Entry entry)
        {
            if (!AnomalyResources.TryGetTexture(entry.TextureHash, out var tex))
            {
                // Texture bytes not available yet; leave pending.
                entry.Applied = false;
                return;
            }

            var original = OverrideApplier.ApplyTexture(key.NetId, key.ComponentPath, key.MaterialIndex, tex, out var material);
            if (material == null)
            {
                // Target not resolvable yet; leave pending.
                entry.Applied = false;
                return;
            }

            // Only store OriginalTexture the first time we apply; subsequent
            // re-applies (e.g. after role change) should not overwrite the true
            // original with the intermediate override texture.
            if (!entry.Applied)
                entry.OriginalTexture = original;

            entry.AppliedMaterial = material;
            entry.Applied = true;

            AssetEvents.OnOverrideApplied(new AssetOverrideAppliedEventArgs(
                key.NetId, key.ComponentPath, key.MaterialIndex, entry.TextureHash));
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
