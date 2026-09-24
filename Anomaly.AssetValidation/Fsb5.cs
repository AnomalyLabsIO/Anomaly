using System.Collections.Generic;
using System.Threading;

namespace Anomaly.AssetValidation;

// Bounded FSB5 framing validation is not a Vorbis decoder or a native-code sandbox.
internal static class Fsb5 {
    private static readonly int[] Frequencies = { 4000, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000 };
    internal static long Validate(ByteSlice bytes, int channels, int frequency, CancellationToken cancellation) {
        var reader = new BoundedReader(bytes.Bytes, bytes.Offset, bytes.Count, cancellation);
        Guard.Require(reader.U32() == 0x35425346 && reader.U32() == 1 && reader.U32() == 1, "Unsupported FSB header.");
        var headers = reader.U32(); var names = reader.U32(); var data = reader.U32();
        Guard.Require(headers >= 20 && headers <= 65536 && (60 + headers) % 32 == 0 && names == 0 && data > 0 && data % 32 == 0 && 60L + headers + data == bytes.Count && reader.U32() == 15, "Invalid FSB sizes or codec.");
        Guard.Require(reader.U32() == 1 && reader.U32() == 0, "Unsupported FSB flags."); reader.Slice(24);
        var packed = reader.U64(); var rateIndex = (int)((packed >> 1) & 15); var encodedChannels = ((packed >> 5) & 1) + 1;
        Guard.Require((packed & 1) == 1 && rateIndex < Frequencies.Length && Frequencies[rateIndex] == frequency && encodedChannels == (ulong)channels && ((packed >> 6) & 0x0fffffff) == 0, "FSB sample metadata mismatch.");
        var frames = (long)(packed >> 34);
        var chunk = reader.U32();
        var chunkSize = (chunk >> 1) & 0x00ffffff;
        Guard.Require((chunk & 1) == 0 && (chunk >> 25) == 11 && chunkSize >= 8 && chunkSize <= 8200, "Unsupported FSB metadata chunk.");
        Guard.Require(reader.U32() != 0, "Unsupported Vorbis setup.");
        var seekBytes = reader.U32();
        Guard.Require(seekBytes <= 8192 && seekBytes % 8 == 0 && chunkSize == 8 + seekBytes, "Invalid Vorbis seek table size.");
        reader.Need((int)seekBytes);
        var seekOffsets = new HashSet<int>(); long previousFrame = -1; long previousOffset = -1;
        for (var i = 0; i < seekBytes / 8; i++) {
            var frame = reader.U32(); var offset = reader.U32();
            Guard.Require(frame > previousFrame && frame < frames && offset > previousOffset && offset < data, "Vorbis seek entry exceeds sample range.");
            previousFrame = frame; previousOffset = offset; seekOffsets.Add((int)offset);
        }
        var padding = bytes.Offset + 60 + (int)headers - reader.Position;
        Guard.Require(padding >= 0 && padding <= 31, "Unexpected FSB header padding."); reader.Zero(padding);
        var dataStart = reader.Position;
        var packets = 0;
        while (reader.Remaining > 0) {
            cancellation.ThrowIfCancellationRequested();
            var packetOffset = reader.Position - dataStart;
            if (reader.Remaining == 1) { reader.Zero(1); break; }
            var size = reader.U16();
            if (size == 0) { Guard.Require(reader.Remaining <= 29, "Unexpected audio tail."); reader.Zero(reader.Remaining); break; }
            Guard.Require(++packets <= 131072 && size <= reader.Remaining, "Vorbis packet budget exceeded.");
            seekOffsets.Remove(packetOffset);
            var packet = reader.Slice(size); Guard.Require((packet.Bytes[packet.Offset] & 1) == 0, "Unexpected Vorbis packet type.");
        }
        Guard.Require(packets > 0 && seekOffsets.Count == 0, "Empty audio or seek target is not a packet boundary."); return frames;
    }
}
