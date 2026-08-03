using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Shared.Assets;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Api.Assets
{
    /// <summary>
    /// Content-addressed, lazy typed loader over <see cref="FileCache"/>. Bytes are
    /// kept on disk by the file-transfer layer; we materialize them as Unity
    /// objects on first request and cache the result until
    /// <see cref="UnloadAll"/> is called (e.g. on disconnect).
    /// </summary>
    public static class AnomalyResources
    {
        private static readonly Dictionary<string, Texture2D> _textures = new();
        private static readonly Dictionary<string, AssetBundle> _bundles = new();
        private static readonly Dictionary<string, AudioClip> _audioClips = new();

        public static bool TryGetTexture(byte[] hash, out Texture2D texture)
        {
            texture = null;
            if (hash == null || hash.Length != 32) return false;

            string hex = FileCache.ToHex(hash);
            if (_textures.TryGetValue(hex, out texture))
            {
                if (texture != null)
                    return true;
                _textures.Remove(hex);
                RemoteAssetDecodeBudget.ReleaseLive("texture:" + hex);
            }

            if (!FileCache.HasFile(hash))
                return false;

            try
            {
                string path = FileCache.GetFilePath(hash);
                if (new FileInfo(path).Length > RemoteAssetDecodeBudget.MaxEncodedTextureBytes)
                {
                    MelonLogger.Warning($"AnomalyResources: Texture {hex} exceeded the encoded decode limit.");
                    return false;
                }
                var bytes = File.ReadAllBytes(path);
                var tex = PngLoader.TryDecode(bytes, "texture:" + hex);
                if (tex == null)
                {
                    MelonLogger.Warning($"AnomalyResources: PNG decode failed for {hex}.");
                    return false;
                }
                _textures[hex] = tex;
                texture = tex;
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AnomalyResources.TryGetTexture({hex}): {ex}");
                return false;
            }
        }

        public static bool TryGetBundle(byte[] hash, out AssetBundle bundle)
        {
            bundle = null;
            if (hash == null || hash.Length != 32) return false;

            string hex = FileCache.ToHex(hash);
            if (_bundles.TryGetValue(hex, out bundle) && bundle != null)
                return true;

            if (!FileCache.HasFile(hash))
                return false;

            try
            {
                var path = FileCache.GetFilePath(hash);
                var b = AssetBundleLoader.LoadFromFile(path);
                if (b == null)
                {
                    MelonLogger.Warning($"AnomalyResources: AssetBundle.LoadFromFile returned null for {hex}.");
                    return false;
                }
                _bundles[hex] = b;
                bundle = b;
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AnomalyResources.TryGetBundle({hex}): {ex}");
                return false;
            }
        }

        public static bool TryGetAudioClip(byte[] hash, out AudioClip clip)
        {
            clip = null;
            if (hash == null || hash.Length != 32) return false;

            string hex = FileCache.ToHex(hash);
            if (_audioClips.TryGetValue(hex, out clip))
            {
                if (clip != null)
                    return true;
                AudioClipFactory.ReleaseStreamer(clip);
                _audioClips.Remove(hex);
            }

            if (!FileCache.HasFile(hash))
                return false;

            try
            {
                string path = FileCache.GetFilePath(hash);
                if (new FileInfo(path).Length > RemoteAssetDecodeBudget.MaxEncodedAudioBytes)
                {
                    MelonLogger.Warning($"AnomalyResources: Audio {hex} exceeded the encoded decode limit.");
                    return false;
                }
                var bytes = File.ReadAllBytes(path);
                var c = AudioClipFactory.TryDecode(
                    bytes,
                    "announcer_" + hex.Substring(0, 8),
                    "audio:" + hex);
                if (c == null)
                {
                    MelonLogger.Warning($"AnomalyResources: AudioClip decode failed for {hex}.");
                    return false;
                }

                _audioClips[hex] = c;
                clip = c;
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AnomalyResources.TryGetAudioClip({hex}): {ex}");
                return false;
            }
        }

        /// <summary>
        /// Loads a named asset out of a cached bundle. Convenience helper that
        /// combines <see cref="TryGetBundle"/> + <c>LoadAsset</c>.
        /// </summary>
        public static bool TryLoadFromBundle<T>(byte[] bundleHash, string assetName, out T asset)
            where T : UnityEngine.Object
        {
            asset = null;
            if (!TryGetBundle(bundleHash, out var bundle))
                return false;

            try
            {
                asset = AssetBundleLoader.LoadAsset<T>(bundle, assetName);
                return asset != null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AnomalyResources.TryLoadFromBundle('{assetName}'): {ex}");
                return false;
            }
        }

        /// <summary>
        /// Drops the cached Unity object for a specific hash so the next lookup
        /// rebuilds it from disk. Used by <see cref="AssetReloadMessage"/> handling
        /// when the underlying file on disk has been replaced.
        /// </summary>
        public static void UnloadOne(byte[] hash)
        {
            if (hash == null) return;
            string hex = FileCache.ToHex(hash);

            if (_textures.TryGetValue(hex, out var tex))
            {
                if (tex != null)
                    UnityEngine.Object.Destroy(tex);
                _textures.Remove(hex);
                RemoteAssetDecodeBudget.ReleaseLive("texture:" + hex);
            }
            if (_bundles.TryGetValue(hex, out var bundle) && bundle != null)
            {
                bundle.Unload(true);
                _bundles.Remove(hex);
            }
            if (_audioClips.TryGetValue(hex, out var clip))
            {
                AudioClipFactory.ReleaseStreamer(clip);
                if (clip != null)
                    UnityEngine.Object.Destroy(clip);
                _audioClips.Remove(hex);
            }
        }

        public static void UnloadAll()
        {
            foreach (var entry in _textures)
            {
                if (entry.Value != null) UnityEngine.Object.Destroy(entry.Value);
                RemoteAssetDecodeBudget.ReleaseLive("texture:" + entry.Key);
            }
            _textures.Clear();

            foreach (var b in _bundles.Values)
                if (b != null) b.Unload(true);
            _bundles.Clear();

            foreach (var c in _audioClips.Values)
            {
                AudioClipFactory.ReleaseStreamer(c);
                if (c != null)
                    UnityEngine.Object.Destroy(c);
            }
            _audioClips.Clear();
        }
    }
}
