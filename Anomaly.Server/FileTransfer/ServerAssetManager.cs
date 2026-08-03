using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.FileTransfer;

public static class ServerAssetManager
{
    private const int MaxScannedFiles = 4096;
    private static readonly Dictionary<string, AssetInfo> Assets = new Dictionary<string, AssetInfo>();
    private static string _assetDirectory;

    public static IReadOnlyDictionary<string, AssetInfo> AllAssets => Assets;

    public static void Init(string assetDirectory)
    {
        Reset();
        _assetDirectory = string.IsNullOrWhiteSpace(assetDirectory)
            ? null
            : Path.GetFullPath(assetDirectory);

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

    public static void Reset()
    {
        Assets.Clear();
        _assetDirectory = null;
    }

    public static void Rescan()
    {
        Assets.Clear();
        ScanAssets();
    }

    /// <summary>
    /// Builds an immutable, session-local view of the advertised files. Transfers resolve only
    /// through this snapshot, never through the live asset dictionary after the manifest is sent.
    /// </summary>
    public static FileManifestMessage BuildManifest(
        ulong sessionId,
        out Dictionary<string, AssetInfo> snapshot)
    {
        var manifest = new FileManifestMessage { SessionId = sessionId };
        snapshot = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
        long aggregateBytes = 0;

        foreach (var kv in Assets)
        {
            if (manifest.Files.Count >= FileManifestMessage.MaxFiles)
                break;

            AssetInfo asset = Clone(kv.Value);
            if (!IsManifestSafe(asset, aggregateBytes, out string reason))
            {
                Logger.Warn($"ServerAssetManager: Not advertising '{asset.RelativePath}': {reason}.");
                continue;
            }

            aggregateBytes += asset.Size;
            snapshot[kv.Key] = asset;
            manifest.Files.Add(new FileManifestMessage.FileEntry(
                asset.RelativePath,
                (byte[])asset.Hash.Clone(),
                asset.Size,
                asset.Kind));
        }

        return manifest;
    }

    public static bool TryGetAsset(byte[] hash, out AssetInfo asset)
    {
        if (hash == null || hash.Length != 32)
        {
            asset = default;
            return false;
        }

        string hex = ToHex(hash);
        return Assets.TryGetValue(hex, out asset);
    }

    private static void ScanAssets()
    {
        if (string.IsNullOrEmpty(_assetDirectory) || !Directory.Exists(_assetDirectory))
            return;

        var files = GetBoundedAssetFiles();
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        int skipped = 0;
        long aggregateBytes = 0;
        using (var sha = SHA256.Create())
        {
            foreach (var filePath in files)
            {
                if (Assets.Count >= FileManifestMessage.MaxFiles)
                {
                    skipped += files.Length - Assets.Count - skipped;
                    break;
                }

                try
                {
                    var info = new FileInfo(filePath);
                    AssetKind kind = InferKind(filePath);
                    string relativePath = GetRelativePath(filePath);
                    int pathBytes = Encoding.UTF8.GetByteCount(relativePath);

                    if (kind == AssetKind.Unknown ||
                        info.Length <= 0 ||
                        info.Length > FileManifestMessage.MaxFileBytes ||
                        pathBytes <= 0 ||
                        pathBytes > FileManifestMessage.MaxPathBytes ||
                        aggregateBytes + info.Length > FileManifestMessage.MaxAggregateBytes)
                    {
                        skipped++;
                        Logger.Warn($"ServerAssetManager: Skipping unsafe or over-budget asset '{relativePath}'.");
                        continue;
                    }

                    byte[] hash;
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        hash = sha.ComputeHash(stream);

                    string hex = ToHex(hash);
                    if (Assets.ContainsKey(hex))
                    {
                        skipped++;
                        Logger.Warn($"ServerAssetManager: Skipping duplicate content hash for '{relativePath}'.");
                        continue;
                    }

                    Assets[hex] = new AssetInfo
                    {
                        FullPath = filePath,
                        RelativePath = relativePath,
                        Hash = hash,
                        Size = checked((uint)info.Length),
                        Kind = kind
                    };
                    aggregateBytes += info.Length;
                }
                catch (Exception ex)
                {
                    skipped++;
                    Logger.Error($"ServerAssetManager: Failed to hash '{filePath}': {ex.Message}");
                }
            }
        }

        Logger.Info(
            $"ServerAssetManager: Scanned {Assets.Count} bounded assets ({aggregateBytes} bytes) " +
            $"from '{_assetDirectory}'; skipped {skipped}.");
    }

    private static string[] GetBoundedAssetFiles()
    {
        var results = new List<string>();
        var pending = new Stack<string>();
        string root = Path.GetFullPath(_assetDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        pending.Push(root.TrimEnd(Path.DirectorySeparatorChar));

        int visitedDirectories = 0;
        while (pending.Count > 0 && results.Count < MaxScannedFiles && visitedDirectories < MaxScannedFiles)
        {
            string directory = pending.Pop();
            visitedDirectories++;
            try
            {
                foreach (string child in Directory.GetDirectories(directory))
                {
                    string fullChild = Path.GetFullPath(child);
                    if (!fullChild.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var info = new DirectoryInfo(fullChild);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        Logger.Warn($"ServerAssetManager: Skipping reparse-point directory '{fullChild}'.");
                        continue;
                    }
                    pending.Push(fullChild);
                }

                foreach (string file in Directory.GetFiles(directory))
                {
                    if (results.Count >= MaxScannedFiles)
                        break;
                    string fullFile = Path.GetFullPath(file);
                    if (!fullFile.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var info = new FileInfo(fullFile);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        Logger.Warn($"ServerAssetManager: Skipping reparse-point file '{fullFile}'.");
                        continue;
                    }
                    results.Add(fullFile);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"ServerAssetManager: Could not enumerate '{directory}': {ex.Message}");
            }
        }

        if (pending.Count > 0 || results.Count >= MaxScannedFiles)
            Logger.Warn($"ServerAssetManager: Asset scan reached its {MaxScannedFiles}-entry traversal limit.");
        return results.ToArray();
    }

    private static bool IsManifestSafe(AssetInfo asset, long aggregateBytes, out string reason)
    {
        if (asset.Hash == null || asset.Hash.Length != 32)
        {
            reason = "invalid hash";
            return false;
        }
        if (asset.Kind == AssetKind.Unknown)
        {
            reason = "unsupported file kind";
            return false;
        }
        if (asset.Size == 0 || asset.Size > FileManifestMessage.MaxFileBytes)
        {
            reason = "file size is outside the session limit";
            return false;
        }
        if (string.IsNullOrEmpty(asset.RelativePath) ||
            Encoding.UTF8.GetByteCount(asset.RelativePath) > FileManifestMessage.MaxPathBytes)
        {
            reason = "path is outside the wire limit";
            return false;
        }
        if (aggregateBytes + asset.Size > FileManifestMessage.MaxAggregateBytes)
        {
            reason = "aggregate manifest budget exceeded";
            return false;
        }

        reason = null;
        return true;
    }

    private static AssetInfo Clone(AssetInfo asset)
    {
        asset.Hash = asset.Hash == null ? null : (byte[])asset.Hash.Clone();
        return asset;
    }

    private static string GetRelativePath(string fullPath)
    {
        if (_assetDirectory.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
            _assetDirectory.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            return fullPath.Substring(_assetDirectory.Length);

        return fullPath.Substring(_assetDirectory.Length + 1);
    }

    internal static AssetKind InferKind(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        switch (ext)
        {
            case ".png":
            case ".jpg":
            case ".jpeg":
                return AssetKind.Texture2D;
            case ".ttf":
            case ".otf":
                return AssetKind.Font;
            case ".unity3d":
            case ".bundle":
            case ".assetbundle":
                return AssetKind.AssetBundle;
            case ".ogg":
            case ".wav":
                return AssetKind.AudioClip;
            default:
                return AssetKind.Unknown;
        }
    }

    internal static string ToHex(byte[] hash) =>
        BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

    public struct AssetInfo
    {
        public string FullPath;
        public string RelativePath;
        public byte[] Hash;
        public uint Size;
        public AssetKind Kind;
    }
}
