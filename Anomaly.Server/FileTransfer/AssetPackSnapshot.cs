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
using Anomaly.Shared.Networking;
using Anomaly.Shared.Scripting;
using Anomaly.Server.Utils;
using Anomaly.Server.Api.Content;
using Anomaly.Shared.Content.Packs;

namespace Anomaly.Server.FileTransfer;

/// <summary>Reference-counted immutable files and metadata, produced only on a background worker.</summary>
internal sealed class AssetPackSnapshot : IDisposable
{
    internal const long MaxSpoolBytes = 512L * 1024 * 1024;
    private static long _spooledBytes;
    private int _references = 1;
    private readonly string _spoolDirectory;
    private long _ownedBytes;
    internal long ContentRegistrationRevision { get; private set; }
    internal PackItemDefinition[] Items { get; private set; } = Array.Empty<PackItemDefinition>();
    internal readonly Dictionary<string, ServerAssetManager.AssetInfo> Files = new Dictionary<string, ServerAssetManager.AssetInfo>(StringComparer.Ordinal);
    private readonly Dictionary<string, AssetRecord> _addresses = new Dictionary<string, AssetRecord>(StringComparer.Ordinal);
    private readonly List<CatalogPack> _packs = new List<CatalogPack>();
    private readonly List<CatalogScriptPack> _sourcePacks = new List<CatalogScriptPack>();
    private readonly Dictionary<string, ScriptPack> _scriptPacks = new Dictionary<string, ScriptPack>(StringComparer.Ordinal);
    internal IEnumerable<AssetRecord> Assets => _addresses.Values;
    private AssetPackSnapshot(string directory) => _spoolDirectory = directory;
    internal static AssetPackSnapshot Empty() => new AssetPackSnapshot(null) { ContentRegistrationRevision = ContentPacks.Revision };

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
    internal bool TryGetScriptSchema(string packId, string name, AnomalyMessageDirection direction,
        out ScriptMessageSchema schema, out string indexHash)
    {
        schema = null;
        indexHash = null;
        if (packId == null || name == null || !_scriptPacks.TryGetValue(packId, out var pack) ||
            !pack.Schemas.TryGetValue(name, out var captured) || captured.Direction != direction)
            return false;
        schema = captured.Clone();
        indexHash = pack.IndexHash;
        return true;
    }
    internal List<CatalogPack> CopyPacks() => _packs.Select(pack => new CatalogPack
    {
        PackId = pack.PackId, IndexHash = (byte[])pack.IndexHash.Clone(), IndexSize = pack.IndexSize,
        BundleHash = (byte[])pack.BundleHash.Clone(), BundleSize = pack.BundleSize, DecodedBytes = pack.DecodedBytes
    }).ToList();
    internal List<CatalogScriptPack> CopyScriptPacks() => _sourcePacks.Select(pack => new CatalogScriptPack
    {
        PackId = pack.PackId, IndexHash = (byte[])pack.IndexHash.Clone(), IndexSize = pack.IndexSize,
        Sources = pack.Sources.Select(source => new CatalogScriptSource
        { Address = source.Address, Hash = (byte[])source.Hash.Clone(), Size = source.Size }).ToList(),
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
        var embedded = ContentPacks.Capture(out long revision);
        string root = Path.GetFullPath(string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(Path.GetTempPath(), "Anomaly-Server-Content-Spool") : directory);
        Directory.CreateDirectory(root);
        ContentPackFiles.CheckAncestors(root);
        string spoolRoot = Path.Combine(root, ".anomaly-spool");
        Directory.CreateDirectory(spoolRoot);
        ContentPackFiles.CheckAncestors(spoolRoot);
        string spool = Path.Combine(spoolRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(spool);
        var result = new AssetPackSnapshot(spool) { ContentRegistrationRevision = revision };
        try
        {
            var packs = ContentPackFiles.Discover(directory,
                    ScriptPackLimits.MaxScriptBytes - embedded.Sum(pack => pack.Contents.SourceBytes?.Length ?? 0),
                    ScriptPackLimits.MaxScripts - embedded.Count(pack => pack.Contents.ScriptIndex != null),
                    ScriptMessageLimits.MaxSchemasPerCatalog - embedded.Sum(pack => pack.Contents.ScriptIndex?.Messages.Count ?? 0))
                .Concat(embedded).OrderBy(pack => pack.Contents.Descriptor.PackId, StringComparer.Ordinal).ToArray();
            ContentPacks.ValidateInventory(packs);
            result.Items = packs.SelectMany(pack => pack.Contents.Items).OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
            foreach (var pack in packs)
            {
                var contents = pack.Contents;
                var index = contents.AssetIndex;
                if (index != null)
                {
                    if (index.BundleBytes <= 0 || index.BundleBytes > FileManifestMessage.MaxFileBytes)
                        throw new InvalidDataException("Bundle exceeds its file budget.");
                    ServerAssetManager.AssetInfo bundle;
                    using (var input = pack.OpenBundle()) bundle = result.CopyBundle(input, index.BundleBytes, index.BundleSha256);
                    var metadata = result.CopyIndex(contents.AssetIndexBytes);
                    result._packs.Add(new CatalogPack
                    {
                        PackId = index.PackId, IndexHash = metadata.Hash, IndexSize = metadata.Size,
                        BundleHash = bundle.Hash, BundleSize = bundle.Size, DecodedBytes = index.DecodedBytes,
                    });
                    foreach (var entry in index.Assets) result.Add(new AssetRecord(index.PackId, metadata.Hash, entry, null, entry.AssetKind, entry.TriangleCount));
                    foreach (var visual in index.Visuals)
                    {
                        long triangles = 0;
                        foreach (var node in visual.Nodes)
                            if (!string.IsNullOrEmpty(node.MeshAddress)) triangles += index.Assets.Single(entry => entry.Address == node.MeshAddress).TriangleCount;
                        result.Add(new AssetRecord(index.PackId, metadata.Hash, null, visual, AssetKind.DecorativeVisual, checked((int)triangles)));
                    }
                }
                if (contents.ScriptIndex != null)
                {
                    var metadata = result.CopyBytes(contents.ScriptIndexBytes, FileContentKind.ScriptIndex, ".scripts.json");
                    var source = contents.ScriptIndex.Sources.Single();
                    if (contents.SourceBytes.Length != 0) result.CopyBytes(contents.SourceBytes, FileContentKind.LuaSource, ".lua");
                    result._sourcePacks.Add(new CatalogScriptPack
                    {
                        PackId = contents.ScriptIndex.PackId, IndexHash = metadata.Hash, IndexSize = metadata.Size,
                        Sources = new List<CatalogScriptSource>
                        {
                            new CatalogScriptSource { Address = source.Address, Hash = ScriptSourceValidator.Sha256(contents.SourceBytes), Size = (uint)contents.SourceBytes.Length },
                        },
                    });
                    result._scriptPacks.Add(contents.ScriptIndex.PackId, new ScriptPack(ServerAssetManager.ToHex(metadata.Hash), contents.ScriptIndex.Messages));
                }
            }
            // Wrapper order cannot establish canonical order for independently named components.
            result._packs.Sort((left, right) => StringComparer.Ordinal.Compare(left.PackId, right.PackId));
            result._sourcePacks.Sort((left, right) => StringComparer.Ordinal.Compare(left.PackId, right.PackId));
            if (result.Files.Count > FileManifestMessage.MaxFiles) throw new InvalidDataException("Content physical file count exceeded its budget.");
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
        => CopyBytes(bytes, FileContentKind.PackIndex, ".index.json");
    private ServerAssetManager.AssetInfo CopyBytes(byte[] bytes, FileContentKind kind, string extension)
    {
        byte[] hash;
        using (var sha = SHA256.Create()) hash = sha.ComputeHash(bytes);
        string hex = ServerAssetManager.ToHex(hash);
        if (Files.TryGetValue(hex, out var existing))
        {
            if (existing.Kind != kind || existing.Size != bytes.Length) throw new InvalidDataException("Conflicting content metadata.");
            return existing;
        }
        Reserve(bytes.Length);
        string name = hex + extension;
        string path = Path.Combine(_spoolDirectory, name);
        File.WriteAllBytes(path, bytes);
        var file = new ServerAssetManager.AssetInfo { FullPath = path, RelativePath = name, Hash = hash, Size = (uint)bytes.Length, Kind = kind };
        Files.Add(hex, file);
        return file;
    }
    private ServerAssetManager.AssetInfo CopyBundle(Stream input, long size, string hex)
    {
        bool reused = Files.TryGetValue(hex, out var existing);
        if (reused && (existing.Kind != FileContentKind.Bundle || existing.Size != size))
            throw new InvalidDataException("Conflicting bundle metadata.");
        if (!reused) Reserve(size);
        string name = hex + ".bundle";
        string path = Path.Combine(_spoolDirectory, name);
        byte[] hash;
        using (Stream output = reused ? Stream.Null : new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
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
        if (reused) return existing;
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
    // All owner reads are explicit descriptor paths through ContentPackFiles.
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

    private sealed class ScriptPack
    {
        internal readonly string IndexHash;
        internal readonly Dictionary<string, ScriptMessageSchema> Schemas = new Dictionary<string, ScriptMessageSchema>(StringComparer.Ordinal);
        internal ScriptPack(string hash, IEnumerable<ScriptMessageSchema> schemas)
        {
            IndexHash = hash;
            foreach (var schema in schemas) Schemas.Add(schema.Name, schema.Clone());
        }
    }
}
