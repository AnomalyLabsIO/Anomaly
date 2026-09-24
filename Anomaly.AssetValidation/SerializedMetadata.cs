using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AssetsTools.NET;

namespace Anomaly.AssetValidation;

internal static class SerializedMetadata {
    internal static List<AssetRecord> Read(UnityFsEnvelope envelope, CancellationToken cancellation) {
        var entry = envelope.Entries[0];
        var reader = new BoundedReader(envelope.Expanded, entry.Offset, entry.Length, cancellation, true);
        reader.Zero(8); Guard.Require(reader.U32() == 22, "Unsupported serialized version."); reader.Zero(4);
        reader.Zero(4); // Little-endian marker and three reserved bytes.
        var metadataSize = reader.U32(); var fileSize = reader.I64(); var dataOffset = reader.I64(); reader.Zero(8);
        Guard.Require(fileSize == entry.Length && metadataSize > 0 && metadataSize <= 256 * 1024 && metadataSize <= entry.Length - 48, "Invalid serialized metadata size.");
        Guard.Require(dataOffset == ((48L + metadataSize + 15) & ~15L) && dataOffset <= entry.Length, "Invalid serialized data offset.");
        var metadata = new BoundedReader(envelope.Expanded, 48, (int)metadataSize, cancellation);
        Guard.Require(metadata.NullString(32) == BundleValidator.UnityVersion && metadata.U32() == 19 && metadata.Boolean(), "Unsupported serialized profile.");
        var typeCount = metadata.I32(); Guard.Require(typeCount >= 1 && typeCount <= 5, "Unsupported type count.");
        var classes = new List<int>(typeCount);
        for (var i = 0; i < typeCount; i++) {
            cancellation.ThrowIfCancellationRequested();
            var id = metadata.I32(); metadata.Position -= 4;
            Guard.Require(CanonicalProfile.Types.TryGetValue(id, out var canonical) && !classes.Contains(id), "Unsupported or duplicate class.");
            metadata.Match(canonical.Record); classes.Add(id);
        }
        var objectCount = metadata.I32(); metadata.Align();
        Guard.Require(objectCount >= 2 && objectCount <= 4096 && objectCount <= metadata.Remaining / 24, "Object count exceeds limit.");
        var ids = new HashSet<long>(); var result = new List<AssetRecord>(objectCount);
        for (var i = 0; i < objectCount; i++) {
            var path = metadata.I64(); var offset = metadata.I64(); var size = metadata.U32(); var type = metadata.I32();
            Guard.Require(path != 0 && ids.Add(path) && type >= 0 && type < classes.Count, "Invalid object identity.");
            Guard.Require(offset >= 0 && (offset & 15) == 0 && size > 0 && offset <= entry.Length - dataOffset && size <= entry.Length - dataOffset - offset, "Invalid object range.");
            result.Add(new AssetRecord(path, classes[type], new ByteSlice(envelope.Expanded, (int)(dataOffset + offset), (int)size)));
        }
        Guard.Require(metadata.I32() == 0 && metadata.I32() == 0 && metadata.I32() == 0, "Scripts, external files, and managed reference types are prohibited.");
        Guard.Require(metadata.NullString(0).Length == 0, "Unexpected user information."); metadata.Finished();
        var padding = new BoundedReader(envelope.Expanded, 48 + (int)metadataSize, (int)dataOffset - 48 - (int)metadataSize, cancellation); padding.Zero(padding.Remaining);
        var cursor = (int)dataOffset;
        foreach (var item in result.OrderBy(value => value.Bytes.Offset)) {
            Guard.Require(item.Bytes.Offset == ((cursor + 15) & ~15), "Unindexed object data.");
            var gap = new BoundedReader(envelope.Expanded, cursor, item.Bytes.Offset - cursor, cancellation); gap.Zero(gap.Remaining);
            cursor = item.Bytes.Offset + item.Bytes.Count;
        }
        Guard.Require(cursor == entry.Length && result.Count(value => value.ClassId == 142) == 1 && classes.All(id => result.Any(value => value.ClassId == id)), "Incomplete serialized object coverage.");

        // Every allocation-driving metadata field and every type byte has already been checked.
        // Keep AssetsTools core as the independent metadata reader, with no bundle decompression or value materialization.
        using (var stream = new MemoryStream(envelope.Expanded, 0, entry.Length, false)) {
            var file = new AssetsFile(); file.Read(stream);
            Guard.Require(file.Metadata.AssetInfos.Count == result.Count, "Metadata reader disagreement.");
            for (var i = 0; i < result.Count; i++) {
                cancellation.ThrowIfCancellationRequested();
                var info = file.Metadata.AssetInfos[i]; var checkedInfo = result[i];
                Guard.Require(info.PathId == checkedInfo.PathId && info.TypeId == checkedInfo.ClassId && info.ByteSize == checkedInfo.Bytes.Count && info.ByteOffset + dataOffset == checkedInfo.Bytes.Offset, "Metadata reader disagreement.");
            }
        }
        return result;
    }
}

internal sealed class AssetRecord {
    internal AssetRecord(long pathId, int classId, ByteSlice bytes) { PathId = pathId; ClassId = classId; Bytes = bytes; Summary = new ValidatedAsset(pathId, classId); Ranges.Add(bytes); }
    internal readonly long PathId;
    internal readonly int ClassId;
    internal readonly ByteSlice Bytes;
    internal readonly ValidatedAsset Summary;
    internal readonly List<ByteSlice> Ranges = new List<ByteSlice>();
    internal readonly HashSet<long> References = new HashSet<long>();
    internal long SpriteTexture;
}
