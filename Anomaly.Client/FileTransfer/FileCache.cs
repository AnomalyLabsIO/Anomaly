using System.Security.Cryptography;
using System.Text.Json;
using Anomaly.Client.API.Features;
using MelonLoader;

namespace Anomaly.Client.FileTransfer;

public static class FileCache
{
    private static readonly object Lock = new();
    private static Dictionary<string, CacheEntry> _index = new();
    private static string _cacheDir;
    private static string _manifestPath;

    public static void Init()
    {
        _cacheDir = Paths.FileCachePath;
        _manifestPath = Path.Combine(_cacheDir, "manifest.json");

        if (!Directory.Exists(_cacheDir))
            Directory.CreateDirectory(_cacheDir);

        LoadIndex();
        MelonLogger.Msg($"FileCache initialized at {_cacheDir} ({_index.Count} cached files)");
    }

    public static bool HasFile(byte[] hash)
    {
        string hex = ToHex(hash);
        lock (Lock)
        {
            if (!_index.TryGetValue(hex, out var entry))
                return false;

            string filePath = GetFilePathInternal(hex);
            if (!File.Exists(filePath))
            {
                _index.Remove(hex);
                return false;
            }

            entry.LastUsed = DateTime.UtcNow;
            _index[hex] = entry;
            return true;
        }
    }

    public static string GetFilePath(byte[] hash)
    {
        return GetFilePathInternal(ToHex(hash));
    }

    public static void WriteFile(byte[] hash, byte[] data, string originalPath)
    {
        string hex = ToHex(hash);
        string filePath = GetFilePathInternal(hex);
        string dir = Path.GetDirectoryName(filePath);

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Atomic write: temp file + rename
        string tempPath = filePath + ".tmp";
        try
        {
            File.WriteAllBytes(tempPath, data);
            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tempPath, filePath);

            lock (Lock)
            {
                _index[hex] = new CacheEntry
                {
                    OriginalPath = originalPath,
                    Size = (uint)data.Length,
                    LastUsed = DateTime.UtcNow
                };
            }

            SaveIndex();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"FileCache.WriteFile failed for {hex}: {ex.Message}");
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public static void Evict(long maxBytes)
    {
        lock (Lock)
        {
            long totalSize = 0;
            foreach (var entry in _index.Values)
                totalSize += entry.Size;

            if (totalSize <= maxBytes)
                return;

            var sorted = _index.OrderBy(kv => kv.Value.LastUsed).ToList();
            foreach (var kv in sorted)
            {
                if (totalSize <= maxBytes)
                    break;

                string filePath = GetFilePathInternal(kv.Key);
                if (File.Exists(filePath))
                    File.Delete(filePath);

                totalSize -= kv.Value.Size;
                _index.Remove(kv.Key);
            }
        }

        SaveIndex();
    }

    public static byte[] ComputeHash(byte[] data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
    }

    public static string ToHex(byte[] hash)
    {
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static string GetFilePathInternal(string hex)
    {
        string prefix = hex.Substring(0, 2);
        return Path.Combine(_cacheDir, prefix, hex);
    }

    private static void LoadIndex()
    {
        if (!File.Exists(_manifestPath))
            return;

        try
        {
            string json = File.ReadAllText(_manifestPath);
            var manifest = JsonSerializer.Deserialize<CacheManifest>(json);
            if (manifest?.Files != null)
                _index = manifest.Files;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"FileCache: Failed to load manifest, starting fresh: {ex.Message}");
            _index = new Dictionary<string, CacheEntry>();
        }
    }

    private static void SaveIndex()
    {
        try
        {
            var manifest = new CacheManifest { Version = 1 };
            lock (Lock)
            {
                manifest.Files = new Dictionary<string, CacheEntry>(_index);
            }
            string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_manifestPath, json);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"FileCache: Failed to save manifest: {ex.Message}");
        }
    }

    private class CacheManifest
    {
        public int Version { get; set; }
        public Dictionary<string, CacheEntry> Files { get; set; } = new();
    }

    public struct CacheEntry
    {
        public string OriginalPath { get; set; }
        public uint Size { get; set; }
        public DateTime LastUsed { get; set; }
    }
}
