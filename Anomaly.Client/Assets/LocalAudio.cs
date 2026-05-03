using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Client.Api.Assets;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Assets
{
    /// <summary>
    /// Loads OGG/WAV files from absolute paths into <see cref="AudioClip"/>
    /// instances with per-path caching. Mirror of <see cref="LocalTextures"/>;
    /// used by <see cref="PathOverrideRegistry"/> to materialize files dropped
    /// in <c>UserData/Anomaly/Overrides/AudioClip/</c>.
    /// </summary>
    public static class LocalAudio
    {
        private static readonly Dictionary<string, AudioClip> _cache = new();

        public static AudioClip TryLoad(string absolutePath)
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
                var clipName = Path.GetFileNameWithoutExtension(absolutePath);
                var clip = AudioClipFactory.TryDecode(bytes, clipName);
                if (clip == null)
                {
                    MelonLogger.Warning($"LocalAudio: decode failed for {absolutePath}.");
                    return null;
                }

                _cache[key] = clip;
                MelonLogger.Msg($"LocalAudio: loaded {absolutePath} ({clip.channels}ch @ {clip.frequency}Hz, {clip.length:F2}s).");
                return clip;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"LocalAudio.TryLoad('{absolutePath}'): {ex}");
                return null;
            }
        }

        public static void UnloadAll()
        {
            foreach (var c in _cache.Values)
            {
                if (c == null) continue;
                AudioClipFactory.ReleaseStreamer(c);
                UnityEngine.Object.Destroy(c);
            }
            _cache.Clear();
        }
    }
}
