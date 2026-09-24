using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Modding;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Il2CppMirror;
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
        private const int MaximumOverrides = TextureOverridePolicy.MaximumOverrides;
        private const int MaximumOverridesPerNetId = TextureOverridePolicy.MaximumOverridesPerObject;

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
            public AssetAddress TextureAddress;
            public Material AppliedMaterial;
            public int TexturePropertyId;
            public Texture OriginalTexture;
            public bool Applied;
        }

        private static readonly Dictionary<Key, Entry> _overrides = new();
        private static readonly RemoteStateCapacityGuard<uint, Key> Capacity =
            new(MaximumOverrides, MaximumOverridesPerNetId);
        private static readonly TransactionalInitializer Initializer = new();

        public static void Init()
        {
            Initializer.Initialize(
                new InitializationStep(
                    () => AnomalyMessaging.MessageReceived += OnMessage,
                    () => AnomalyMessaging.MessageReceived -= OnMessage),
                // After the initial file transfer completes any pending overrides
                // that were waiting on a not-yet-cached texture hash might now be
                // resolvable.
                new InitializationStep(
                    () => FileTransferEvents.Complete += HandleTransferComplete,
                    () => FileTransferEvents.Complete -= HandleTransferComplete));
        }

        private static void HandleTransferComplete(FileTransferCompleteEventArgs _) => ReapplyAll();

        /// <summary>
        /// Re-resolve and re-apply all overrides. Call this after events that
        /// replace the GameObject hierarchy under a stable <c>netId</c>, e.g. a
        /// player role change. Safe to call redundantly.
        /// </summary>
        public static void ReapplyAll()
        {
            foreach (var kv in new List<KeyValuePair<Key, Entry>>(_overrides))
            {
                Restore(kv.Value, $"reapply for netId {kv.Key.NetId}");
                Apply(kv.Key, kv.Value);
            }
        }

        /// <summary>
        /// Re-resolve and re-apply every override keyed to a specific NetId.
        /// Called from <c>NetworkIdentity.OnStartClient</c> patches when a new
        /// object spawns, and safe to call from mod code after known role swaps.
        /// </summary>
        public static void ReapplyForNetId(uint netId)
        {
            foreach (var kv in new List<KeyValuePair<Key, Entry>>(_overrides))
            {
                if (kv.Key.NetId == netId)
                {
                    Restore(kv.Value, $"object replacement for netId {kv.Key.NetId}");
                    Apply(kv.Key, kv.Value);
                }
            }
        }

        /// <summary>
        /// Restores every override back to its cached original. Called on
        /// disconnect so reconnecting to a vanilla server doesn't leave stale
        /// custom textures bleeding through.
        /// </summary>
        public static void Clear()
        {
            var snapshot = new List<KeyValuePair<Key, Entry>>(_overrides);
            _overrides.Clear();
            Capacity.Clear();
            var failures = new List<Exception>();

            foreach (var kv in snapshot)
            {
                try { Restore(kv.Value, $"disconnect cleanup for netId {kv.Key.NetId}"); }
                catch (Exception exception)
                {
                    _overrides[kv.Key] = kv.Value;
                    failures.Add(exception);
                    try { MelonLogger.Warning($"OverrideRegistry isolated cleanup failure: {exception}"); }
                    catch { }
                }
            }
            if (failures.Count > 0) throw new AggregateException("Texture bindings could not be restored before bundle unload.", failures);
        }

        private static void OnMessage(Shared.Networking.IAnomalyMessage msg)
        {
            switch (msg)
            {
                case AssetOverrideTextureMessage ot: HandleOverride(ot); break;
                case AssetClearOverrideMessage co: HandleClearOne(co); break;
                case AssetClearAllOverridesMessage cao: HandleClearAll(cao); break;
            }
        }

        private static void HandleOverride(AssetOverrideTextureMessage msg)
        {
            if (msg.NetId == 0 ||
                !Anomaly.Client.Content.ContentCatalogClient.IsActivated ||
                !AssetAddress.TryParse(msg.TextureAddress, out var textureAddress) ||
                !AssetBundleManager.IsRemoteAsset(textureAddress, AssetKind.Texture2D))
            {
                RejectRemoteState("received a texture override outside the activated manifest");
                return;
            }

            var key = new Key(msg.NetId, msg.ComponentPath, msg.MaterialIndex);
            if (!Capacity.TryAdd(msg.NetId, key))
            {
                RejectRemoteState(
                    $"texture override capacity exceeded ({MaximumOverrides} total, " +
                    $"{MaximumOverridesPerNetId} per object)");
                return;
            }

            // If one was applied previously, revert first so we don't lose the
            // "original before any override" reference.
            if (_overrides.TryGetValue(key, out var existing))
                Restore(existing, $"replacement for netId {key.NetId}");

            var entry = new Entry { TextureAddress = textureAddress };
            _overrides[key] = entry;
            Apply(key, entry);
        }

        private static void HandleClearOne(AssetClearOverrideMessage msg)
        {
            var key = new Key(msg.NetId, msg.ComponentPath, msg.MaterialIndex);
            if (!_overrides.TryGetValue(key, out var entry))
                return;

            Restore(entry, $"clear for netId {key.NetId}");
            _overrides.Remove(key);
            Capacity.Remove(key.NetId, key);
            AssetEvents.OnOverrideCleared(new AssetOverrideClearedEventArgs(msg.NetId, msg.ComponentPath, msg.MaterialIndex));
        }

        private static void HandleClearAll(AssetClearAllOverridesMessage msg)
        {
            var toRemove = new List<KeyValuePair<Key, Entry>>();
            foreach (var kv in new List<KeyValuePair<Key, Entry>>(_overrides))
            {
                if (kv.Key.NetId != msg.NetId) continue;
                toRemove.Add(kv);
            }
            Capacity.RemoveScope(msg.NetId);
            foreach (var pair in toRemove)
            {
                Restore(pair.Value, $"clear-all for netId {pair.Key.NetId}");
                _overrides.Remove(pair.Key);
                AssetEvents.OnOverrideCleared(new AssetOverrideClearedEventArgs(
                    pair.Key.NetId,
                    pair.Key.ComponentPath,
                    pair.Key.MaterialIndex));
            }
        }

        private static void Restore(Entry entry, string context)
        {
            if (!entry.Applied)
                return;

            bool restored = false;
            try
            {
                if (entry.AppliedMaterial != null)
                    entry.AppliedMaterial.SetTexture(entry.TexturePropertyId, entry.OriginalTexture);
                AssetLifetime.Release(entry.OriginalTexture);
                restored = true;
            }
            catch (Exception exception)
            {
                try { MelonLogger.Warning($"OverrideRegistry failed to restore {context}: {exception}"); }
                catch { }
                throw;
            }
            finally
            {
                if (restored)
                {
                    entry.Applied = false;
                    entry.AppliedMaterial = null;
                    entry.OriginalTexture = null;
                }
            }
        }

        private static void RejectRemoteState(string reason)
        {
            // Revoke logical capacity and restore every applied override before
            // relying on a disconnect callback that may never arrive.
            try { Clear(); }
            catch { Capacity.Clear(); }
            try { AnomalyMessaging.ClearTransportSession(); }
            catch { /* disconnect remains mandatory */ }
            try { NetworkClient.connection?.Disconnect(); }
            catch { /* the transport may already be closing */ }
            try
            {
                FeatureHealth.Report("anomaly", "networking", FeatureHealthStatus.Failed, reason);
                MelonLogger.Error("Authenticated remote state was rejected: " + reason);
            }
            catch { /* diagnostics must not weaken fail-closed teardown */ }
        }

        private static void Apply(Key key, Entry entry)
        {
            if (!AnomalyResources.TryGetTexture(entry.TextureAddress, out var tex))
            {
                // Texture bytes not available yet; leave pending.
                entry.Applied = false;
                return;
            }

            var original = OverrideApplier.ApplyTexture(key.NetId, key.ComponentPath, key.MaterialIndex, tex, out var material, out int texturePropertyId);
            if (material == null)
            {
                // Target not resolvable yet; leave pending.
                entry.Applied = false;
                return;
            }

            // Only store OriginalTexture the first time we apply; subsequent
            // re-applies (e.g. after role change) should not overwrite the true
            // original with the intermediate override texture.
            entry.OriginalTexture = original;
            entry.AppliedMaterial = material;
            entry.TexturePropertyId = texturePropertyId;
            entry.Applied = true;
            try { AssetLifetime.Retain(original); }
            catch { Restore(entry, "retention failure"); throw; }

            AssetEvents.OnOverrideApplied(new AssetOverrideAppliedEventArgs(
                key.NetId, key.ComponentPath, key.MaterialIndex, entry.TextureAddress));
        }

    }
}
