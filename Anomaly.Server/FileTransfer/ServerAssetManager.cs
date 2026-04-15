using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Anomaly.Shared.Networking.Messages;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.FileTransfer;

public static class ServerAssetManager
{
    private static readonly Dictionary<string, AssetInfo> Assets = new Dictionary<string, AssetInfo>();
    private static string _assetDirectory;

    public static IReadOnlyDictionary<string, AssetInfo> AllAssets => Assets;

    public static void Init(string assetDirectory)
    {
        _assetDirectory = assetDirectory;

        if (string.IsNullOrEmpty(_assetDirectory))
        {
            Logger.Info("ServerAssetManager: No asset directory configured. File transfer disabled.");
            return;
        }

        if (!Directory.Exists(_assetDirectory))
        {
            Logger.Info($"ServerAssetManager: Asset directory '{_assetDirectory}' does not exist. Creating it.");
            Directory.CreateDirectory(_assetDirectory);
            return;
        }

        ScanAssets();
    }

    public static void Rescan()
    {
        Assets.Clear();
        ScanAssets();
    }

    public static FileManifestMessage BuildManifest()
    {
        var manifest = new FileManifestMessage();
        foreach (var kv in Assets)
        {
            manifest.Files.Add(new FileManifestMessage.FileEntry(
                kv.Value.RelativePath, kv.Value.Hash, kv.Value.Size));
        }
        return manifest;
    }

    public static bool TryGetAsset(byte[] hash, out AssetInfo asset)
    {
        string hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return Assets.TryGetValue(hex, out asset);
    }

    private static void ScanAssets()
    {
        if (string.IsNullOrEmpty(_assetDirectory) || !Directory.Exists(_assetDirectory))
            return;

        var files = Directory.GetFiles(_assetDirectory, "*", SearchOption.AllDirectories);
        using (var sha = SHA256.Create())
        {
            foreach (var filePath in files)
            {
                try
                {
                    var data = File.ReadAllBytes(filePath);
                    var hash = sha.ComputeHash(data);
                    string hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    string relativePath = GetRelativePath(filePath);

                    Assets[hex] = new AssetInfo
                    {
                        FullPath = filePath,
                        RelativePath = relativePath,
                        Hash = hash,
                        Size = (uint)data.Length
                    };
                }
                catch (Exception ex)
                {
                    Logger.Error($"ServerAssetManager: Failed to hash '{filePath}': {ex.Message}");
                }
            }
        }

        Logger.Info($"ServerAssetManager: Scanned {Assets.Count} assets from '{_assetDirectory}'.");
    }

    private static string GetRelativePath(string fullPath)
    {
        if (_assetDirectory.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
            _assetDirectory.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            return fullPath.Substring(_assetDirectory.Length);

        return fullPath.Substring(_assetDirectory.Length + 1);
    }

    public struct AssetInfo
    {
        public string FullPath;
        public string RelativePath;
        public byte[] Hash;
        public uint Size;
    }
}
