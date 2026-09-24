using System;
using System.Threading;

namespace Anomaly.AssetValidation;

// Independently implemented from https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md.
// In particular, reject zero offsets and incomplete output before native Unity decompression.
internal static class StrictLz4 {
    internal static void Decode(ByteSlice source, byte[] destination, int offset, int count, CancellationToken cancellation) {
        Guard.Require(offset >= 0 && count >= 0 && offset <= destination.Length - count, "Invalid LZ4 destination.");
        var input = new BoundedReader(source.Bytes, source.Offset, source.Count, cancellation);
        var output = offset;
        var end = offset + count;
        var lastMatch = -1;
        while (input.Remaining != 0) {
            cancellation.ThrowIfCancellationRequested();
            var token = input.U8();
            var literals = Length(input, token >> 4, end - output);
            var bytes = input.Slice(literals);
            Buffer.BlockCopy(bytes.Bytes, bytes.Offset, destination, output, literals);
            output += literals;
            if (input.Remaining == 0) {
                Guard.Require(output == end && (count < 5 || literals >= 5) && (lastMatch < 0 || end - lastMatch >= 12), "Invalid LZ4 end sequence.");
                return;
            }
            var distance = input.U16();
            Guard.Require(distance != 0 && distance <= output - offset, "Invalid LZ4 match distance.");
            Guard.Require(end - output >= 4, "LZ4 output exceeds limit.");
            var match = Length(input, token & 15, end - output - 4) + 4;
            lastMatch = output;
            for (var i = 0; i < match; i++) destination[output + i] = destination[output + i - distance];
            output += match;
        }
        throw new BundleValidationException("Missing LZ4 final sequence.");
    }
    private static int Length(BoundedReader reader, int length, int maximum) {
        Guard.Require(length <= maximum, "LZ4 output exceeds limit.");
        if (length == 15) {
            byte next;
            do { next = reader.U8(); Guard.Require(next <= maximum - length, "LZ4 output exceeds limit."); length += next; } while (next == 255);
        }
        return length;
    }
}
