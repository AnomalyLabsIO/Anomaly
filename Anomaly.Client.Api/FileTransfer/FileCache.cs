using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Anomaly.Client.Api.Features;
using MelonLoader;

namespace Anomaly.Client.Api.FileTransfer;

internal static class FileCache
{
    private static readonly object Lock = new();
    private static readonly object IndexWriteLock = new();
    private static Dictionary<string, CacheEntry> _index = new();
    private static string _cacheDir;
    private static string _manifestPath;

    public static void Init()
    {
        _cacheDir = Paths.FileCachePath;
        _manifestPath = Path.Combine(_cacheDir, "manifest.json");

        if (!Directory.Exists(_cacheDir))
            Directory.CreateDirectory(_cacheDir);

        _index = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        CleanupIncomingFiles();
        LoadIndex();
        ReconcileIndex();
        QuarantineOverBudget(500L * 1024L * 1024L);
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

        // Atomic same-directory replacement: a reader sees either the old blob
        // or the fully flushed new blob, never a partial file.
        string tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data, 0, data.Length);
                stream.Flush(true);
            }
            lock (Lock)
            {
                File.Move(tempPath, filePath, overwrite: true);
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
        uint expectedSize) => ImportVerifiedFile(tempPath, expectedHash, originalPath, expectedSize, CancellationToken.None);

    internal static bool ImportVerifiedFile(
        string tempPath,
        byte[] expectedHash,
        string originalPath,
        uint expectedSize,
        CancellationToken cancellation)
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

            cancellation.ThrowIfCancellationRequested();
            byte[] actualHash;
            using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
            using (var sha = SHA256.Create())
            {
                var block = new byte[81920];
                int count;
                while ((count = stream.Read(block, 0, block.Length)) != 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    sha.TransformBlock(block, 0, count, block, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                actualHash = sha.Hash;
            }

            if (!actualHash.AsSpan().SequenceEqual(expectedHash))
                return false;

            string dir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            lock (Lock)
            {
                cancellation.ThrowIfCancellationRequested();
                // Serialize replacement and index publication with cache readers
                // and eviction. Hashing and manifest I/O stay outside this lock.
                File.Move(sourcePath, filePath, overwrite: true);
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
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
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
            if (manifest?.Version == 1 && manifest.Files != null)
                _index = manifest.Files;
            else
                throw new InvalidDataException("FileCache manifest schema is unsupported or incomplete.");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"FileCache: Failed to load manifest; rebuilding from verified blobs: {ex.Message}");
            QuarantineFile(_manifestPath, "manifest");
            _index = new Dictionary<string, CacheEntry>();
        }
    }

    private static void ReconcileIndex()
    {
        var reconciled = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(_cacheDir, "*", SearchOption.AllDirectories))
        {
            string fullPath = Path.GetFullPath(path);
            if (string.Equals(fullPath, Path.GetFullPath(_manifestPath), StringComparison.OrdinalIgnoreCase) ||
                fullPath.Contains(Path.DirectorySeparatorChar + ".incoming" + Path.DirectorySeparatorChar) ||
                fullPath.Contains(Path.DirectorySeparatorChar + ".quarantine" + Path.DirectorySeparatorChar) ||
                fullPath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                continue;

            if (fullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                QuarantineFile(path, "temporary");
                continue;
            }

            string name = Path.GetFileName(path);
            if (!IsLowerHexSha256(name))
            {
                QuarantineFile(path, "unexpected");
                continue;
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > 64L * 1024L * 1024L)
                {
                    QuarantineFile(path, "size");
                    continue;
                }

                byte[] actual;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
                using (var sha = SHA256.Create())
                    actual = sha.ComputeHash(stream);
                if (!string.Equals(ToHex(actual), name, StringComparison.Ordinal))
                {
                    QuarantineFile(path, "hash");
                    continue;
                }

                if (!_index.TryGetValue(name, out CacheEntry existing))
                {
                    QuarantineFile(path, "orphan");
                    continue;
                }
                reconciled[name] = new CacheEntry
                {
                    OriginalPath = existing.OriginalPath ?? string.Empty,
                    Size = checked((uint)info.Length),
                    LastUsed = existing.LastUsed == default ? info.LastWriteTimeUtc : existing.LastUsed,
                    VerifiedLastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                };
            }
            catch (Exception exception)
            {
                MelonLogger.Warning($"FileCache: Could not reconcile '{path}': {exception.Message}");
                QuarantineFile(path, "read");
            }
        }

        lock (Lock)
            _index = reconciled;
        SaveIndex();
    }

    private static void CleanupIncomingFiles()
    {
        string incomingDir = Path.Combine(_cacheDir, ".incoming");
        if (!Directory.Exists(incomingDir))
            return;

        int quarantined = 0;
        foreach (string file in Directory.GetFiles(incomingDir, "*.part", SearchOption.TopDirectoryOnly))
        {
            try
            {
                QuarantineFile(file, "temporary");
                quarantined++;
            }
            catch
            {
                // Best effort; active transfers have not started during cache initialization.
            }
        }

        if (quarantined > 0)
            MelonLogger.Msg($"FileCache: Quarantined {quarantined} stale partial download(s).");
    }

    private static void SaveIndex()
    {
        lock (IndexWriteLock)
        {
            string temporary = null;
            try
            {
                CacheManifest manifest;
                lock (Lock) manifest = new CacheManifest
                {
                    Version = 1,
                    Files = new Dictionary<string, CacheEntry>(_index, StringComparer.Ordinal),
                };
                string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                temporary = _manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                string backup = _manifestPath + ".bak";
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(_manifestPath))
                    File.Replace(temporary, _manifestPath, backup, ignoreMetadataErrors: true);
                else
                    File.Move(temporary, _manifestPath);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"FileCache: Failed to save manifest: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (temporary != null && File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch
                {
                    // A stale temp is quarantined during the next startup reconciliation.
                }
            }
        }
    }

    private static void QuarantineOverBudget(long maxBytes)
    {
        bool changed = false;
        lock (Lock)
        {
            long totalSize = _index.Values.Sum(entry => (long)entry.Size);
            if (totalSize <= maxBytes)
                return;

            foreach (KeyValuePair<string, CacheEntry> entry in
                     _index.OrderBy(pair => pair.Value.LastUsed).ToList())
            {
                if (totalSize <= maxBytes)
                    break;
                QuarantineFile(GetFilePathInternal(entry.Key), "budget");
                totalSize -= entry.Value.Size;
                _index.Remove(entry.Key);
                changed = true;
            }
        }

        if (changed)
            SaveIndex();
    }

    private static bool IsLowerHexSha256(string value) =>
        value != null && value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void QuarantineFile(string path, string reason)
    {
        try
        {
            if (!File.Exists(path))
                return;
            string directory = Path.Combine(_cacheDir, ".quarantine");
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(
                directory,
                Path.GetFileName(path) + "." + reason + "." + DateTime.UtcNow.Ticks);
            File.Move(path, destination);
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"FileCache: Could not quarantine '{path}': {exception.Message}");
        }
    }

    private class CacheManifest
    {
        public int Version { get; set; }
        public Dictionary<string, CacheEntry> Files { get; set; } = new();
    }

    private struct CacheEntry
    {
        public string OriginalPath { get; set; }
        public uint Size { get; set; }
        public DateTime LastUsed { get; set; }
        public long VerifiedLastWriteUtcTicks { get; set; }
    }
}
