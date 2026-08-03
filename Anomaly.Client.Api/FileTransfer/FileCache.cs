using System.Security.Cryptography;
using System.Text.Json;
using Anomaly.Client.Api.Features;
using MelonLoader;

namespace Anomaly.Client.Api.FileTransfer;

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

        CleanupIncomingFiles();
        LoadIndex();
        MelonLogger.Msg($"FileCache initialized at {_cacheDir} ({_index.Count} cached files)");
    }

    public static bool HasFile(byte[] hash)
    {
        string hex = ToHex(hash);
        lock (Lock)
        {
            try
            {
                if (!_index.TryGetValue(hex, out var entry))
                    return false;

                string filePath = GetFilePathInternal(hex);
                if (!File.Exists(filePath))
                {
                    _index.Remove(hex);
                    return false;
                }

                var info = new FileInfo(filePath);
                if (info.Length != entry.Size)
                {
                    MelonLogger.Warning($"FileCache: Size verification failed for {hex}; requesting a fresh copy.");
                    _index.Remove(hex);
                    return false;
                }
                if (entry.VerifiedLastWriteUtcTicks != info.LastWriteTimeUtc.Ticks)
                {
                    byte[] actualHash;
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
                    using (var sha = SHA256.Create())
                        actualHash = sha.ComputeHash(stream);
                    if (!actualHash.AsSpan().SequenceEqual(hash))
                    {
                        MelonLogger.Warning($"FileCache: SHA-256 verification failed for {hex}; requesting a fresh copy.");
                        _index.Remove(hex);
                        return false;
                    }
                    entry.VerifiedLastWriteUtcTicks = info.LastWriteTimeUtc.Ticks;
                }

                entry.LastUsed = DateTime.UtcNow;
                _index[hex] = entry;
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"FileCache: Verification failed for {hex}: {ex.Message}");
                _index.Remove(hex);
                return false;
            }
        }
    }

    public static string GetFilePath(byte[] hash)
    {
        return GetFilePathInternal(ToHex(hash));
    }

    public static void WriteFile(byte[] hash, byte[] data, string originalPath)
    {
        if (hash == null || hash.Length != 32 || data == null || data.Length == 0 ||
            data.Length > 64 * 1024 * 1024 ||
            !ComputeHash(data).AsSpan().SequenceEqual(hash))
        {
            MelonLogger.Warning("FileCache.WriteFile rejected data that did not match its content hash.");
            return;
        }

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
                    LastUsed = DateTime.UtcNow,
                    VerifiedLastWriteUtcTicks = new FileInfo(filePath).LastWriteTimeUtc.Ticks
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

    /// <summary>
    /// Verifies and atomically imports a completed streaming download into the
    /// content-addressed cache without materializing the whole file in memory.
    /// The source file must be on the same volume as the cache so the final move
    /// cannot expose a partially-written destination.
    /// </summary>
    public static bool ImportVerifiedFile(
        string tempPath,
        byte[] expectedHash,
        string originalPath,
        uint expectedSize)
    {
        if (string.IsNullOrWhiteSpace(tempPath) ||
            expectedHash == null ||
            expectedHash.Length != 32 ||
            expectedSize == 0 ||
            expectedSize > 64u * 1024u * 1024u ||
            !File.Exists(tempPath))
        {
            return false;
        }

        string hex = ToHex(expectedHash);
        string filePath = GetFilePathInternal(hex);
        string cacheRoot = Path.GetFullPath(_cacheDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string sourcePath = Path.GetFullPath(tempPath);
        if (!sourcePath.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase))
        {
            MelonLogger.Warning("FileCache.ImportVerifiedFile rejected a temp file outside the cache root.");
            return false;
        }

        try
        {
            var info = new FileInfo(sourcePath);
            if (info.Length != expectedSize)
                return false;

            byte[] actualHash;
            using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
            using (var sha = SHA256.Create())
                actualHash = sha.ComputeHash(stream);

            if (!actualHash.AsSpan().SequenceEqual(expectedHash))
                return false;

            string dir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.Move(sourcePath, filePath, overwrite: true);

            lock (Lock)
            {
                _index[hex] = new CacheEntry
                {
                    OriginalPath = originalPath,
                    Size = expectedSize,
                    LastUsed = DateTime.UtcNow,
                    VerifiedLastWriteUtcTicks = new FileInfo(filePath).LastWriteTimeUtc.Ticks
                };
            }

            SaveIndex();
            return true;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"FileCache.ImportVerifiedFile failed for {hex}: {ex.Message}");
            return false;
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
        if (hash == null || hash.Length != 32)
            throw new ArgumentException("Content hash must be 32 bytes.", nameof(hash));
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

    private static void CleanupIncomingFiles()
    {
        string incomingDir = Path.Combine(_cacheDir, ".incoming");
        if (!Directory.Exists(incomingDir))
            return;

        int removed = 0;
        foreach (string file in Directory.GetFiles(incomingDir, "*.part", SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch
            {
                // Best effort; active transfers have not started during cache initialization.
            }
        }

        if (removed > 0)
            MelonLogger.Msg($"FileCache: Removed {removed} stale partial download(s).");
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
        public long VerifiedLastWriteUtcTicks { get; set; }
    }
}
