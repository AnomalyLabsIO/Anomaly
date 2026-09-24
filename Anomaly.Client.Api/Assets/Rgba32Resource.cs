using System.Text;

namespace Anomaly.Client.Api.Assets;

/// <summary>Fixed offline format: ARGBA32\0, little-endian dimensions, bottom-up RGBA pixels.</summary>
internal static class Rgba32Resource
{
    internal static byte[] Read(Stream source, out int width, out int height)
    {
        if (source == null || !source.CanRead || !source.CanSeek || source.Length < 16)
            throw new InvalidDataException("Invalid RGBA32 resource stream.");
        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "ARGBA32\0")
            throw new InvalidDataException("Unknown RGBA32 resource format.");
        width = reader.ReadInt32();
        height = reader.ReadInt32();
        if (width <= 0 || width > 4096 || height <= 0 || height > 4096)
            throw new InvalidDataException("RGBA32 resource dimensions exceed the texture budget.");
        int length = checked(width * height * 4);
        if (source.Length - source.Position != length)
            throw new InvalidDataException("RGBA32 resource pixel length does not match its dimensions.");
        byte[] pixels = reader.ReadBytes(length);
        if (pixels.Length != length) throw new EndOfStreamException("Truncated RGBA32 resource.");
        return pixels;
    }
}
