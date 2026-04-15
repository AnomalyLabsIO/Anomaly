using System;
using System.Collections.Generic;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Assets;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Assets
{
    /// <summary>
    /// Plugin-facing lookup layer over <see cref="ServerAssetManager"/>. Provides
    /// convenience accessors to resolve content-addressed asset hashes from the
    /// relative paths modders actually care about.
    /// </summary>
    public static class AssetRegistry
    {
        /// <summary>
        /// Returns the 32-byte SHA-256 hash for the asset at the given relative
        /// path (relative to the server's configured asset directory), or null
        /// if no asset with that path is registered.
        /// </summary>
        public static byte[] GetHashByPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return null;

            var normalized = Normalize(relativePath);
            foreach (var kv in ServerAssetManager.AllAssets)
            {
                if (Normalize(kv.Value.RelativePath) == normalized)
                    return kv.Value.Hash;
            }
            return null;
        }

        public static bool TryGetKind(byte[] hash, out AssetKind kind)
        {
            if (ServerAssetManager.TryGetAsset(hash, out var info))
            {
                kind = info.Kind;
                return true;
            }
            kind = AssetKind.Unknown;
            return false;
        }

        public static bool TryGetRelativePath(byte[] hash, out string relativePath)
        {
            if (ServerAssetManager.TryGetAsset(hash, out var info))
            {
                relativePath = info.RelativePath;
                return true;
            }
            relativePath = null;
            return false;
        }

        public static IEnumerable<(string RelativePath, byte[] Hash, AssetKind Kind)> All()
        {
            foreach (var kv in ServerAssetManager.AllAssets)
            {
                yield return (kv.Value.RelativePath, kv.Value.Hash, kv.Value.Kind);
            }
        }

        public static void LogSummary()
        {
            int tex = 0, font = 0, bundle = 0, unk = 0;
            foreach (var kv in ServerAssetManager.AllAssets)
            {
                switch (kv.Value.Kind)
                {
                    case AssetKind.Texture2D: tex++; break;
                    case AssetKind.Font: font++; break;
                    case AssetKind.AssetBundle: bundle++; break;
                    default: unk++; break;
                }
            }
            Logger.Info($"AssetRegistry: {tex} textures, {font} fonts, {bundle} bundles, {unk} unknown.");
        }

        private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
    }
}
