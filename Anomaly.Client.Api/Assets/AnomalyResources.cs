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
    /// Loads approved server textures and audio by SHA-256. Returned Unity
    /// objects are shared, borrowed resources for the current connection; mods
    /// must not destroy them. The framework releases them during teardown.
    /// Call these methods on the Unity main thread.
    /// </summary>
    public static class AnomalyResources
    {
        private static readonly Dictionary<string, Texture2D> _textures = new();
        private static readonly Dictionary<string, AudioClip> _audioClips = new();
        private static Func<byte[], AssetKind, bool> _remoteAssetAuthorization;

        internal static void SetRemoteAssetAuthorization(Func<byte[], AssetKind, bool> authorization) =>
            _remoteAssetAuthorization = authorization;

        internal static bool IsRemoteAssetAuthorized(byte[] hash, AssetKind kind)
        {
            if (hash == null || hash.Length != 32)
                return false;
            try
            {
                return _remoteAssetAuthorization?.Invoke(hash, kind) == true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Gets an approved PNG texture, or false if unavailable or invalid.</summary>
        public static bool TryGetTexture(byte[] hash, out Texture2D texture)
        {
            texture = null;
            if (!IsRemoteAssetAuthorized(hash, AssetKind.Texture2D)) return false;

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

        /// <summary>Gets an approved OGG audio clip, or false if unavailable or invalid.</summary>
        public static bool TryGetAudioClip(byte[] hash, out AudioClip clip)
        {
            clip = null;
            if (!IsRemoteAssetAuthorized(hash, AssetKind.AudioClip)) return false;

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
                var c = AudioClipFactory.TryDecodeRemoteOgg(
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
        /// Drops the cached Unity object for a specific hash so the next lookup
        /// rebuilds it from disk.
        /// </summary>
        internal static void UnloadOne(byte[] hash)
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
            if (_audioClips.TryGetValue(hex, out var clip))
            {
                AudioClipFactory.ReleaseStreamer(clip);
                if (clip != null)
                    UnityEngine.Object.Destroy(clip);
                _audioClips.Remove(hex);
            }
        }

        internal static void UnloadAll()
        {
            foreach (var entry in _textures)
            {
                if (entry.Value != null) UnityEngine.Object.Destroy(entry.Value);
                RemoteAssetDecodeBudget.ReleaseLive("texture:" + entry.Key);
            }
            _textures.Clear();

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
