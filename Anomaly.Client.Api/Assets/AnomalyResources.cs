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

        public static bool TryGetTexture(byte[] hash, out Texture2D texture)
        {
            texture = null;
            if (hash == null || hash.Length != 32) return false;

            string hex = FileCache.ToHex(hash);
            if (_textures.TryGetValue(hex, out texture) && texture != null)
                return true;

            if (!FileCache.HasFile(hash))
                return false;

            try
            {
                var bytes = File.ReadAllBytes(FileCache.GetFilePath(hash));
                var tex = PngLoader.TryDecode(bytes);
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
                var b = AssetBundle.LoadFromFile(path);
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
                asset = bundle.LoadAsset<T>(assetName);
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

            if (_textures.TryGetValue(hex, out var tex) && tex != null)
            {
                UnityEngine.Object.Destroy(tex);
                _textures.Remove(hex);
            }
            if (_bundles.TryGetValue(hex, out var bundle) && bundle != null)
            {
                bundle.Unload(true);
                _bundles.Remove(hex);
            }
        }

        public static void UnloadAll()
        {
            foreach (var t in _textures.Values)
                if (t != null) UnityEngine.Object.Destroy(t);
            _textures.Clear();

            foreach (var b in _bundles.Values)
                if (b != null) b.Unload(true);
            _bundles.Clear();
        }
    }
}
