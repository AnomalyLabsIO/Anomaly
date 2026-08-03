using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Client -> server. Sent when the local player wants the server to record
    /// their chosen jumpsuit tint (on encrypted-channel ready, and again any
    /// time the local color changes mid-game). Sender identity is implicit
    /// from the transport, so no NetId or UserId is included on the wire.
    /// </summary>
    public class SetFavoriteColorMessage : AuthenticatedClientToServerMessage, IAnomalyMessage
    {
        public override int MaxPayloadBytes => 3;
        public string MessageName => "anomaly:player:setfavoritecolor";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(R);
            writer.Write(G);
            writer.Write(B);
        }

        public void Deserialize(BinaryReader reader)
        {
            R = reader.ReadByte();
            G = reader.ReadByte();
            B = reader.ReadByte();
        }
    }
}
