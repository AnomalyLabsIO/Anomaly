using System.IO;
using Anomaly.Shared.Networking;

namespace HelloAnomaly.Protocol;

// Link this same source into both plugins so names, directions and codecs agree.
public sealed class HelloRequest : AuthenticatedClientToServerMessage, IAnomalyMessage
{
    public const string Name = "example.hello:request";
    public string MessageName => Name;
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public override int MaxPayloadBytes => 4;
    public uint Token { get; set; }
    public void Serialize(BinaryWriter writer) => writer.Write(Token);
    public void Deserialize(BinaryReader reader) => Token = reader.ReadUInt32();
}

public sealed class HelloReply : AuthenticatedServerToClientMessage, IAnomalyMessage
{
    public const string Name = "example.hello:reply";
    public string MessageName => Name;
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public override int MaxPayloadBytes => 5;
    public uint Token { get; set; }
    public bool ContentActivated { get; set; }
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Token);
        writer.Write(ContentActivated);
    }
    public void Deserialize(BinaryReader reader)
    {
        Token = reader.ReadUInt32();
        ContentActivated = reader.ReadBoolean();
    }
}
