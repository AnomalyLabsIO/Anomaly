using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Server.Utils;

namespace Anomaly.Server.FileTransfer;

/// <summary>Reference-counted immutable files and metadata, produced only on a background worker.</summary>
internal sealed class AssetPackSnapshot : IDisposable
{
    internal const int MaxIndexBytes = AssetPackIndexCodec.MaxIndexBytes;
    internal const int MaxScannedEntries = 4096;
    internal const long MaxSpoolBytes = 512L * 1024 * 1024;
    private static long _spooledBytes;
    private int _references = 1;
    private readonly string _spoolDirectory;
    private long _ownedBytes;
    internal readonly Dictionary<string, ServerAssetManager.AssetInfo> Files = new Dictionary<string, ServerAssetManager.AssetInfo>(StringComparer.Ordinal);
    private readonly Dictionary<string, AssetRecord> _addresses = new Dictionary<string, AssetRecord>(StringComparer.Ordinal);
    private readonly List<CatalogPack> _packs = new List<CatalogPack>();
    internal IEnumerable<AssetRecord> Assets => _addresses.Values;
    private AssetPackSnapshot(string directory) => _spoolDirectory = directory;
    internal static AssetPackSnapshot Empty() => new AssetPackSnapshot(null);

    internal AssetPackSnapshot Retain()
    {
        int count;
        do { count = Volatile.Read(ref _references); if (count <= 0) throw new ObjectDisposedException(nameof(AssetPackSnapshot)); }
        while (Interlocked.CompareExchange(ref _references, count + 1, count) != count);
        return this;
    }
    public void Dispose()
    {
        if (Interlocked.Decrement(ref _references) != 0 || _spoolDirectory == null) return;
        Task.Run(() =>
        {
            try { Directory.Delete(_spoolDirectory, true); Interlocked.Add(ref _spooledBytes, -_ownedBytes); }
            catch (Exception exception) { OffThreadLogger.Warn("Asset spool retained after cleanup failure: " + exception.Message); }
        });
    }
    internal bool TryGetAsset(AssetAddress address, out AssetRecord record) => _addresses.TryGetValue(address.Value, out record);
    internal bool Contains(AssetAddress address, AssetKind kind) => TryGetAsset(address, out var record) && record.Kind == kind;
    internal List<CatalogPack> CopyPacks() => _packs.Select(pack => new CatalogPack
    {
        PackId = pack.PackId, IndexHash = (byte[])pack.IndexHash.Clone(), IndexSize = pack.IndexSize,
        BundleHash = (byte[])pack.BundleHash.Clone(), BundleSize = pack.BundleSize
    }).ToList();
    internal FileManifestMessage BuildManifest(ulong sessionId)
    {
        var message = new FileManifestMessage { SessionId = sessionId };
        foreach (var file in Files.Values.OrderBy(value => value.RelativePath, StringComparer.Ordinal))
            message.Files.Add(new FileManifestMessage.FileEntry(file.RelativePath, (byte[])file.Hash.Clone(), file.Size, file.Kind));
        message.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(message.Files);
        return message;
    }
    internal static AssetPackSnapshot Build(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return Empty();
        string root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        RejectReparsePoint(root);
        string spoolRoot = Path.Combine(root, ".anomaly-spool");
        Directory.CreateDirectory(spoolRoot);
        RejectReparsePoint(spoolRoot);
        string spool = Path.Combine(spoolRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(spool);
        var result = new AssetPackSnapshot(spool);
        try
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            long aggregate = 0;
            foreach (string indexPath in EnumerateIndexes(root).OrderBy(path => path, StringComparer.Ordinal))
            {
                if (ids.Count >= FileManifestMessage.MaxFiles / 2) throw new InvalidDataException("Too many packs.");
                byte[] bytes = ReadIndex(indexPath);
                AssetPackIndex index = AssetPackIndexCodec.Parse(bytes, true);
                if (!ids.Add(index.PackId)) throw new InvalidDataException("Duplicate pack ID: " + index.PackId);
                string bundlePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(indexPath), index.BundleFile));
                if (!string.Equals(Path.GetDirectoryName(bundlePath), Path.GetDirectoryName(indexPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Bundle must be beside its index.");
                RejectReparsePoint(bundlePath);
                aggregate += index.BundleBytes + bytes.Length;
                if (index.BundleBytes <= 0 || index.BundleBytes > FileManifestMessage.MaxFileBytes || aggregate > FileManifestMessage.MaxAggregateBytes)
                    throw new InvalidDataException("Pack file/aggregate budget exceeded.");
                var bundle = result.CopyBundle(bundlePath, index.BundleBytes, index.BundleSha256);
                var metadata = result.CopyIndex(bytes);
                result._packs.Add(new CatalogPack { PackId = index.PackId, IndexHash = metadata.Hash, IndexSize = metadata.Size, BundleHash = bundle.Hash, BundleSize = bundle.Size });
                foreach (var entry in index.Assets) result.Add(new AssetRecord(index.PackId, metadata.Hash, entry, null, entry.AssetKind, entry.TriangleCount));
                foreach (var visual in index.Visuals)
                {
                    long triangles = 0;
                    foreach (var node in visual.Nodes)
                        if (!string.IsNullOrEmpty(node.MeshAddress)) triangles += index.Assets.Single(entry => entry.Address == node.MeshAddress).TriangleCount;
                    result.Add(new AssetRecord(index.PackId, metadata.Hash, null, visual, AssetKind.DecorativeVisual, checked((int)triangles)));
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    private void Add(AssetRecord record)
    {
        if (_addresses.ContainsKey(record.Address.Value)) throw new InvalidDataException("Duplicate asset address: " + record.Address);
        _addresses.Add(record.Address.Value, record);
    }
    private ServerAssetManager.AssetInfo CopyIndex(byte[] bytes)
    {
        byte[] hash;
        using (var sha = SHA256.Create()) hash = sha.ComputeHash(bytes);
        string hex = ServerAssetManager.ToHex(hash);
        if (Files.TryGetValue(hex, out var existing)) return existing;
        Reserve(bytes.Length);
        string name = hex + ".index.json";
        string path = Path.Combine(_spoolDirectory, name);
        File.WriteAllBytes(path, bytes);
        var file = new ServerAssetManager.AssetInfo { FullPath = path, RelativePath = name, Hash = hash, Size = (uint)bytes.Length, Kind = FileContentKind.PackIndex };
        Files.Add(hex, file);
        return file;
    }
    private ServerAssetManager.AssetInfo CopyBundle(string source, long size, string hex)
    {
        if (Files.TryGetValue(hex, out var existing))
        {
            if (existing.Kind != FileContentKind.Bundle || existing.Size != size) throw new InvalidDataException("Conflicting bundle metadata.");
            return existing;
        }
        Reserve(size);
        string name = hex + ".bundle";
        string path = Path.Combine(_spoolDirectory, name);
        byte[] hash;
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var sha = SHA256.Create())
        {
            if (input.Length != size) throw new InvalidDataException("Bundle size does not match its index.");
            byte[] buffer = new byte[65536];
            long copied = 0;
            int count;
            while ((count = input.Read(buffer, 0, (int)Math.Min(buffer.Length, size - copied + 1))) > 0)
            {
                copied += count;
                if (copied > size) throw new InvalidDataException("Bundle grew while copying.");
                output.Write(buffer, 0, count);
                sha.TransformBlock(buffer, 0, count, buffer, 0);
            }
            if (copied != size) throw new InvalidDataException("Bundle was truncated while copying.");
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            hash = sha.Hash;
            if (ServerAssetManager.ToHex(hash) != hex) throw new InvalidDataException("Bundle SHA-256 does not match its index.");
        }
        var file = new ServerAssetManager.AssetInfo { FullPath = path, RelativePath = name, Hash = hash, Size = checked((uint)size), Kind = FileContentKind.Bundle };
        Files.Add(hex, file);
        return file;
    }
    private void Reserve(long bytes)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref _spooledBytes);
            if (current + bytes > MaxSpoolBytes) throw new IOException("Retained snapshots reached the 512 MiB spool limit.");
        } while (Interlocked.CompareExchange(ref _spooledBytes, current + bytes, current) != current);
        _ownedBytes += bytes;
    }
    private static byte[] ReadIndex(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length <= 0 || stream.Length > MaxIndexBytes) throw new InvalidDataException("Index size exceeds its budget.");
            var bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) throw new EndOfStreamException("Incomplete index.");
                offset += read;
            }
            if (stream.ReadByte() != -1) throw new InvalidDataException("Index grew while reading.");
            return bytes;
        }
    }
    private static void RejectReparsePoint(string path)
    { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Asset paths cannot be reparse points."); }
    private static IEnumerable<string> EnumerateIndexes(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        int visited = 0;
        while (pending.Count > 0)
            foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (++visited > MaxScannedEntries) throw new IOException("Asset directory exceeds traversal budget.");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                { if (Path.GetFileName(path) != ".anomaly-spool") pending.Push(path); }
                else if (path.EndsWith(".index.json", StringComparison.OrdinalIgnoreCase)) yield return path;
            }
    }
    internal sealed class AssetRecord
    {
        internal readonly string PackId;
        internal readonly byte[] IndexHash;
        internal readonly PackAssetEntry Entry;
        internal readonly DecorativeVisualDefinition Visual;
        internal readonly AssetKind Kind;
        internal readonly int TriangleCount;
        internal AssetAddress Address => AssetAddress.Parse(Entry?.Address ?? Visual.Address);
        internal AssetRecord(string packId, byte[] hash, PackAssetEntry entry, DecorativeVisualDefinition visual, AssetKind kind, int triangles)
        { PackId = packId; IndexHash = hash; Entry = entry; Visual = visual; Kind = kind; TriangleCount = triangles; }
    }
}
