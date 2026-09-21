using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace Anomaly.EarlyAccessUiSmoke;

// The supported game's generated ImageConversion bindings cannot marshal the
// native result. Encode the detached raw screenshot without touching game state.
internal static class ScreenshotPng
{
    internal static byte[] Encode(int width, int height, TextureFormat format, byte[] pixels)
    {
        int channels = format switch
        {
            TextureFormat.RGB24 => 3,
            TextureFormat.RGBA32 => 4,
            _ => throw new NotSupportedException("Screenshot texture format: " + format)
        };
        int stride = checked(width * channels);
        if (pixels.Length != checked(stride * height)) throw new InvalidDataException("Unexpected screenshot byte count.");
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        using var header = new MemoryStream();
        WriteInt(header, width);
        WriteInt(header, height);
        header.Write(new byte[] { 8, (byte)(channels == 4 ? 6 : 2), 0, 0, 0 });
        Chunk(output, "IHDR", header.ToArray());
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest, true))
        {
            for (int row = height - 1; row >= 0; row--)
            {
                zlib.WriteByte(0);
                zlib.Write(pixels, row * stride, stride);
            }
        }
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] payload)
    {
        var tag = Encoding.ASCII.GetBytes(type);
        WriteInt(output, payload.Length);
        output.Write(tag);
        output.Write(payload);
        uint crc = uint.MaxValue;
        foreach (byte value in tag.Concat(payload))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        WriteInt(output, unchecked((int)~crc));
    }

    private static void WriteInt(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
