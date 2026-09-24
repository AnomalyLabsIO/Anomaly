using System;
using System.IO;
using System.Text;
using Anomaly.Shared.Networking;

namespace Anomaly.ServerApiSmoke;

public sealed class SmokeRequest : AuthenticatedClientToServerMessage, IAnomalyMessage
{
    public const string Name = "anomaly-smoke:request";
    public override int MaxPayloadBytes => 4096;
    public string MessageName => Name;
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public int Token { get; set; }
    public string Command { get; set; } = "status";
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Token);
        SmokeWire.WriteText(writer, Command, 2048);
    }
    public void Deserialize(BinaryReader reader)
    {
        Token = reader.ReadInt32();
        Command = SmokeWire.ReadText(reader, 2048);
    }
}

public sealed class SmokeResponse : AuthenticatedServerToClientMessage, IAnomalyMessage
{
    public const string Name = "anomaly-smoke:response";
    public override int MaxPayloadBytes => 4096;
    public string MessageName => Name;
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public int Token { get; set; }
    public bool Success { get; set; }
    /// <summary>JSON object containing the server-observed results for this request.</summary>
    public string Details { get; set; } = "{}";
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Token);
        writer.Write(Success);
        SmokeWire.WriteText(writer, Details, 4089);
    }
    public void Deserialize(BinaryReader reader)
    {
        Token = reader.ReadInt32();
        Success = reader.ReadBoolean();
        Details = SmokeWire.ReadText(reader, 4089);
    }
}

internal static class SmokeWire
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    internal static void WriteText(BinaryWriter writer, string value, int maximum)
    {
        byte[] bytes = Utf8.GetBytes(value ?? string.Empty);
        if (bytes.Length > maximum) throw new InvalidDataException("Smoke text exceeded its bound.");
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }
    internal static string ReadText(BinaryReader reader, int maximum)
    {
        int length = reader.ReadUInt16();
        if (length > maximum) throw new InvalidDataException("Smoke text exceeded its bound.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Utf8.GetString(bytes);
    }
}
