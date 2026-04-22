using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Client.Api.Assets;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Assets
{
    /// <summary>
    /// Loads PNG files from absolute paths into <see cref="Texture2D"/> instances
    /// with per-path caching. Kept separate from <c>AnomalyResources</c> because
    /// that is hash-addressed server-cache material; this is for local user files.
    /// Decoding is delegated to <see cref="PngLoader"/>.
    /// </summary>
    public static class LocalTextures
    {
        private static readonly Dictionary<string, Texture2D> _cache = new();

        public static Texture2D TryLoad(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
                return null;

            string key = absolutePath.ToLowerInvariant();
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            if (!File.Exists(absolutePath))
                return null;

            try
            {
                byte[] bytes = File.ReadAllBytes(absolutePath);
                var tex = PngLoader.TryDecode(bytes);
                if (tex == null)
                {
                    MelonLogger.Warning($"LocalTextures: PNG decode failed for {absolutePath}.");
                    return null;
                }

                _cache[key] = tex;
                MelonLogger.Msg($"LocalTextures: loaded {absolutePath} ({tex.width}x{tex.height}).");
                return tex;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"LocalTextures.TryLoad('{absolutePath}'): {ex}");
                return null;
            }
        }

        public static void UnloadAll()
        {
            foreach (var t in _cache.Values)
                if (t != null) UnityEngine.Object.Destroy(t);
            _cache.Clear();
        }
    }
}
