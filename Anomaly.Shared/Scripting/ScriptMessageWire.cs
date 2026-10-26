using System;
using System.IO;
using System.Text;
using Anomaly.Shared.Assets;

namespace Anomaly.Shared.Scripting;

internal static class ScriptMessageWire
{
    internal const int MaxEnvelopeBytes = 2 + 128 + 1 + 64 + 32 + 8 + 2 + ScriptMessagePayload.MaxEncodedBytes;
    private const string UnboundIndexHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal static void Validate(string packId, string name, ScriptMessagePayload payload)
    {
        if (!AssetPackIndexCodec.IsPackId(packId, remote: false) || !ScriptMessageSchema.IsName(name) || payload == null)
            throw new InvalidDataException("Invalid script message envelope.");
    }

    internal static void ValidateScope(string indexSha256, ulong activationId)
    {
        if (!AssetPackIndexCodec.IsHash(indexSha256) || indexSha256 == UnboundIndexHash || activationId == 0)
            throw new InvalidDataException("Script message requires a bound pack index and activation.");
    }

    internal static void Write(BinaryWriter writer, IScriptMessage message)
    {
        Validate(message.PackId, message.Name, message.Payload);
        ValidateScope(message.IndexSha256, message.ActivationId);
        byte[] packBytes = Utf8.GetBytes(message.PackId);
        byte[] nameBytes = Utf8.GetBytes(message.Name);
        writer.Write((ushort)packBytes.Length); writer.Write(packBytes);
        writer.Write((byte)nameBytes.Length); writer.Write(nameBytes);
        for (int offset = 0; offset < message.IndexSha256.Length; offset += 2)
            writer.Write((byte)((Hex(message.IndexSha256[offset]) << 4) | Hex(message.IndexSha256[offset + 1])));
        writer.Write(message.ActivationId);
        writer.Write((ushort)message.Payload.EncodedBytes);
        message.Payload.Write(writer);
    }

    internal static void Read(BinaryReader reader, out string packId, out string name,
        out string indexSha256, out ulong activationId, out ScriptMessagePayload payload)
    {
        packId = ReadText(reader, reader.ReadUInt16(), 128);
        name = ReadText(reader, reader.ReadByte(), ScriptMessageSchema.MaxNameLength);
        byte[] hash = reader.ReadBytes(32);
        if (hash.Length != 32) throw new EndOfStreamException();
        indexSha256 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        activationId = reader.ReadUInt64();
        ValidateScope(indexSha256, activationId);
        if (!AssetPackIndexCodec.IsPackId(packId, remote: false) || !ScriptMessageSchema.IsName(name))
            throw new InvalidDataException("Invalid script message envelope.");
        payload = ScriptMessagePayload.Read(reader, reader.ReadUInt16());
    }

    private static string ReadText(BinaryReader reader, int length, int maximum)
    {
        if (length < 1 || length > maximum) throw new InvalidDataException("Invalid script message identity length.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        try { return Utf8.GetString(bytes); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Invalid script message identity UTF-8.", exception); }
    }

    private static int Hex(char value) => value <= '9' ? value - '0' : value - 'a' + 10;
}
