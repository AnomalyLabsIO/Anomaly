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
using Anomaly.Server.Api.Scripting;

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
    internal long ScriptRegistrationRevision { get; private set; }
    internal readonly Dictionary<string, ServerAssetManager.AssetInfo> Files = new Dictionary<string, ServerAssetManager.AssetInfo>(StringComparer.Ordinal);
    private readonly Dictionary<string, AssetRecord> _addresses = new Dictionary<string, AssetRecord>(StringComparer.Ordinal);
    private readonly List<CatalogPack> _packs = new List<CatalogPack>();
    private readonly List<CatalogScriptPack> _sourcePacks = new List<CatalogScriptPack>();
    private readonly Dictionary<string, ScriptPack> _scriptPacks = new Dictionary<string, ScriptPack>(StringComparer.Ordinal);
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
        BundleHash = (byte[])pack.BundleHash.Clone(), BundleSize = pack.BundleSize
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
    internal static AssetPackSnapshot Build(string directory, string scriptDirectory = null)
    {
        ClientScripts.Registration[] embedded = ClientScripts.Capture(out long scriptRevision);
        if (string.IsNullOrWhiteSpace(directory) && string.IsNullOrWhiteSpace(scriptDirectory) && embedded.Length == 0)
            return new AssetPackSnapshot(null) { ScriptRegistrationRevision = scriptRevision };
        string root = Path.GetFullPath(!string.IsNullOrWhiteSpace(directory) ? directory :
            !string.IsNullOrWhiteSpace(scriptDirectory) ? scriptDirectory : Path.Combine(Path.GetTempPath(), "Anomaly-Server-Script-Spool"));
        Directory.CreateDirectory(root);
        RejectReparsePoint(root);
        string spoolRoot = Path.Combine(root, ".anomaly-spool");
        Directory.CreateDirectory(spoolRoot);
        RejectReparsePoint(spoolRoot);
        string spool = Path.Combine(spoolRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(spool);
        var result = new AssetPackSnapshot(spool) { ScriptRegistrationRevision = scriptRevision };
        try
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            long aggregate = 0;
            foreach (string indexPath in (string.IsNullOrWhiteSpace(directory) ? Enumerable.Empty<string>() : EnumerateIndexes(Path.GetFullPath(directory))).OrderBy(path => path, StringComparer.Ordinal))
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
                result._packs.Add(new CatalogPack
                {
                    PackId = index.PackId, IndexHash = metadata.Hash, IndexSize = metadata.Size,
                    BundleHash = bundle.Hash, BundleSize = bundle.Size,
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
            int scriptCount = 0, scriptBytes = 0, messageCount = 0;
            foreach (ClientScripts.Registration pack in DiscoverSources(scriptDirectory).Concat(embedded).OrderBy(pack => pack.Index.PackId, StringComparer.Ordinal))
            {
                if (!ids.Add(pack.Index.PackId)) throw new InvalidDataException("Duplicate pack ID: " + pack.Index.PackId);
                ScriptPackIndexCodec.Validate(pack.Index);
                scriptCount = checked(scriptCount + pack.Index.Sources.Count);
                scriptBytes = checked(scriptBytes + pack.Index.Sources.Sum(source => source.SourceBytes));
                messageCount = checked(messageCount + pack.Index.Messages.Count);
                if (scriptCount > ScriptPackLimits.MaxScripts || scriptBytes > ScriptPackLimits.MaxScriptBytes || messageCount > ScriptMessageLimits.MaxSchemasPerCatalog)
                    throw new InvalidDataException("Connection script source or schema inventory exceeded its budget.");
                byte[] bytes = ScriptPackIndexCodec.Encode(pack.Index);
                aggregate += bytes.Length + pack.Index.Sources.Sum(source => (long)source.SourceBytes);
                if (aggregate > FileManifestMessage.MaxAggregateBytes) throw new InvalidDataException("Content aggregate budget exceeded.");
                ServerAssetManager.AssetInfo metadata = result.CopyBytes(bytes, FileContentKind.ScriptIndex, ".scripts.json");
                var offer = new CatalogScriptPack { PackId = pack.Index.PackId, IndexHash = metadata.Hash, IndexSize = metadata.Size };
                foreach (ClientScripts.Source source in pack.Sources.OrderBy(source => source.Address, StringComparer.Ordinal))
                {
                    byte[] hash = ScriptSourceValidator.Sha256(source.Bytes);
                    if (source.Bytes.Length != 0) result.CopyBytes(source.Bytes, FileContentKind.LuaSource, ".lua");
                    offer.Sources.Add(new CatalogScriptSource { Address = source.Address, Hash = hash, Size = checked((uint)source.Bytes.Length) });
                }
                result._sourcePacks.Add(offer);
                result._scriptPacks.Add(pack.Index.PackId, new ScriptPack(ServerAssetManager.ToHex(metadata.Hash), pack.Index.Messages));
            }
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
    private static IEnumerable<ClientScripts.Registration> DiscoverSources(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) yield break;
        string root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        RejectReparsePoint(root);
        var grouped = new Dictionary<string, List<ClientScripts.Source>>(StringComparer.Ordinal);
        var descriptors = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<Tuple<string, int>>();
        pending.Push(Tuple.Create(root, 0));
        int visited = 0, count = 0, sourceBytes = 0;
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        while (pending.Count != 0)
        {
            var parent = pending.Pop();
            foreach (string path in Directory.EnumerateFileSystemEntries(parent.Item1))
            {
                if (++visited > MaxScannedEntries) throw new InvalidDataException("Script directory traversal budget exceeded.");
                if (Path.GetFileName(path) == ".anomaly-spool") continue;
                RejectReparsePoint(path);
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                {
                    if (parent.Item2 >= 16) throw new InvalidDataException("Script directory nesting budget exceeded.");
                    pending.Push(Tuple.Create(path, parent.Item2 + 1));
                    continue;
                }
                string relative = path.Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                bool lua = relative.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
                bool descriptor = Path.GetFileName(path).Equals("messages.json", StringComparison.OrdinalIgnoreCase);
                if (!lua && !descriptor) continue;
                string[] parts = relative.Split('/');
                if (parts.Length < 3 || (descriptor && parts.Length != 3)) throw new InvalidDataException("Scripts must be below namespace/pack; messages.json must be beside pack sources.");
                string packId = (parts[0] + ":" + parts[1]).ToLowerInvariant();
                if (!AssetPackIndexCodec.IsPackId(packId, true)) throw new InvalidDataException("Invalid script directory pack ID.");
                if (descriptor)
                {
                    if (descriptors.ContainsKey(packId)) throw new InvalidDataException("Duplicate script message descriptor.");
                    descriptors.Add(packId, path);
                    continue;
                }
                if (++count > ScriptPackLimits.MaxScripts) throw new InvalidDataException("Script source count exceeded its budget.");
                byte[] bytes;
                using (Stream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) bytes = ClientScripts.ReadBounded(stream, ScriptPackLimits.MaxScriptBytesPerFile);
                ScriptSourceValidator.Validate(bytes);
                sourceBytes = checked(sourceBytes + bytes.Length);
                if (sourceBytes > ScriptPackLimits.MaxScriptBytes) throw new InvalidDataException("Script source bytes exceeded their budget.");
                if (!grouped.TryGetValue(packId, out List<ClientScripts.Source> sources)) grouped.Add(packId, sources = new List<ClientScripts.Source>());
                sources.Add(new ClientScripts.Source("mods/" + relative.ToLowerInvariant(), bytes));
            }
        }
        foreach (string id in descriptors.Keys)
            if (!grouped.ContainsKey(id)) throw new InvalidDataException("A script message descriptor has no Lua sources: " + id);
        foreach (var pair in grouped)
        {
            var index = new ScriptPackIndex { PackId = pair.Key };
            foreach (ClientScripts.Source source in pair.Value.OrderBy(source => source.Address, StringComparer.Ordinal))
                index.Sources.Add(new ScriptPackSource { Address = source.Address, SourceBytes = source.Bytes.Length, SourceSha256 = ScriptSourceValidator.HashHex(source.Bytes) });
            if (descriptors.TryGetValue(pair.Key, out string descriptorPath))
                using (Stream stream = File.Open(descriptorPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    index.Messages = ScriptPackIndexCodec.ParseMessageSchemas(ClientScripts.ReadBounded(stream, ScriptPackLimits.MaxIndexBytes));
            ScriptPackIndexCodec.Validate(index);
            yield return new ClientScripts.Registration(index, pair.Value);
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
