using System;
using System.Collections.Generic;
using System.Threading;

namespace Anomaly.AssetValidation;

internal sealed class UnityFsEnvelope {
    internal byte[] Expanded;
    internal readonly List<BundleBlock> Blocks = new List<BundleBlock>();
    internal readonly List<BundleEntry> Entries = new List<BundleEntry>();

    internal static UnityFsEnvelope Read(byte[] bytes, CancellationToken cancellation) {
        cancellation.ThrowIfCancellationRequested();
        var reader = new BoundedReader(bytes, 0, bytes.Length, cancellation, true);
        Guard.Require(reader.NullString(7) == "UnityFS" && reader.U32() == 8, "Unsupported bundle format.");
        Guard.Require(reader.NullString(16) == "5.x.x" && reader.NullString(32) == BundleValidator.UnityVersion, "Unsupported Unity version.");
        Guard.Require(reader.I64() == bytes.Length, "Bundle length mismatch.");
        var compressedInfo = reader.U32(); var expandedInfo = reader.U32();
        Guard.Require(reader.U32() == 0x243 && compressedInfo > 0 && compressedInfo <= 65536 && expandedInfo > 0 && expandedInfo <= 65536, "Unsupported bundle flags or metadata size.");
        reader.Align(16);
        var info = new byte[(int)expandedInfo];
        StrictLz4.Decode(reader.Slice((int)compressedInfo), info, 0, info.Length, cancellation);
        reader.Align(16);
        var metadata = new BoundedReader(info, 0, info.Length, cancellation, true);
        metadata.Slice(16); // Unity's content hash is not authentication; SHA-256 binds the complete file.
        var blockCount = metadata.I32();
        Guard.Require(blockCount > 0 && blockCount <= 1024 && blockCount <= metadata.Remaining / 10, "Block count exceeds limit.");
        var result = new UnityFsEnvelope();
        var expandedTotal = 0; var compressedTotal = 0;
        for (var i = 0; i < blockCount; i++) {
            var expanded = metadata.U32(); var compressed = metadata.U32(); var flags = metadata.U16();
            Guard.Require(expanded > 0 && expanded <= 131072 && (i == blockCount - 1 || expanded == 131072), "Unsupported block size.");
            Guard.Require(compressed > 0 && compressed <= 131600 && (flags == 0 || flags == 3) && (flags != 0 || expanded == compressed), "Unsupported block compression.");
            Guard.Require(expanded <= BundleValidator.MaximumExpandedBytes - expandedTotal && compressed <= bytes.Length - compressedTotal, "Bundle expansion exceeds limit.");
            result.Blocks.Add(new BundleBlock(expandedTotal, (int)expanded, (int)compressed, flags));
            expandedTotal += (int)expanded; compressedTotal += (int)compressed;
        }
        Guard.Require(compressedTotal == reader.Remaining, "Compressed block coverage mismatch.");
        var entryCount = metadata.I32();
        Guard.Require(entryCount >= 1 && entryCount <= 3, "Unsupported bundle entry count.");
        var covered = 0;
        for (var i = 0; i < entryCount; i++) {
            var offset = metadata.I64(); var size = metadata.I64(); var flags = metadata.U32(); var name = metadata.NullString(64);
            Guard.Require(offset == covered && size > 0 && size <= expandedTotal - covered, "Invalid bundle entry range.");
            Guard.Require(i == 0 ? flags == 4 && IsCab(name) : flags == 0 && (name == result.Entries[0].Name + ".resS" || name == result.Entries[0].Name + ".resource"), "Unexpected bundle entry.");
            foreach (var previous in result.Entries) Guard.Require(previous.Name != name, "Duplicate bundle entry.");
            result.Entries.Add(new BundleEntry(name, covered, (int)size)); covered += (int)size;
        }
        metadata.Finished(); Guard.Require(covered == expandedTotal, "Unindexed bundle data.");
        cancellation.ThrowIfCancellationRequested();
        result.Expanded = new byte[expandedTotal];
        foreach (var block in result.Blocks) {
            cancellation.ThrowIfCancellationRequested();
            var input = reader.Slice(block.CompressedBytes);
            if (block.Flags == 0) Buffer.BlockCopy(bytes, input.Offset, result.Expanded, block.Offset, block.Length);
            else StrictLz4.Decode(input, result.Expanded, block.Offset, block.Length, cancellation);
        }
        return result;
    }
    private static bool IsCab(string name) {
        if (name.Length != 36 || !name.StartsWith("CAB-", StringComparison.Ordinal)) return false;
        for (var i = 4; i < name.Length; i++) if (!(name[i] >= '0' && name[i] <= '9') && !(name[i] >= 'a' && name[i] <= 'f')) return false;
        return true;
    }
    internal long CompressedCost(List<ByteSlice> ranges) {
        long cost = 0;
        foreach (var block in Blocks) {
            foreach (var range in ranges) {
                if (range.Count != 0 && range.Offset < block.Offset + block.Length && block.Offset < range.Offset + range.Count) { cost += block.CompressedBytes; break; }
            }
        }
        return cost;
    }
}

internal sealed class BundleEntry {
    internal BundleEntry(string name, int offset, int length) { Name = name; Offset = offset; Length = length; }
    internal readonly string Name;
    internal readonly int Offset;
    internal readonly int Length;
    internal readonly List<ByteSlice> Used = new List<ByteSlice>();
}

internal sealed class BundleBlock {
    internal BundleBlock(int offset, int length, int compressedBytes, ushort flags) { Offset = offset; Length = length; CompressedBytes = compressedBytes; Flags = flags; }
    internal readonly int Offset;
    internal readonly int Length;
    internal readonly int CompressedBytes;
    internal readonly ushort Flags;
}
