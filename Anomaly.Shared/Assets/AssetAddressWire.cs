using System.IO;
using System.Text;

namespace Anomaly.Shared.Assets;

internal static class AssetAddressWire
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    internal static void Write(BinaryWriter writer, string value)
    {
        if (!AssetAddress.TryParse(value, out _)) throw new InvalidDataException("Invalid asset address.");
        byte[] bytes = Utf8.GetBytes(value);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }
    internal static string Read(BinaryReader reader)
    {
        int length = reader.ReadUInt16();
        if (length == 0 || length > AssetAddress.MaxLength) throw new InvalidDataException("Asset address exceeds its wire limit.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException("Truncated asset address.");
        string value = Utf8.GetString(bytes);
        if (!AssetAddress.TryParse(value, out _)) throw new InvalidDataException("Invalid asset address.");
        return value;
    }
}
