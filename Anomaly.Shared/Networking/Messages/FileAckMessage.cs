using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class FileAckMessage : AuthenticatedClientToServerMessage, IAnomalyMessage
    {
        public override int MaxPayloadBytes => 8 + 32 + 1;
        public string MessageName => "anomaly:fileack";
        public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

        public ulong SessionId { get; set; }
        public byte[] Hash { get; set; }
        public byte Status { get; set; } // 0 = success, 1 = failure

        public void Serialize(BinaryWriter writer)
        {
            if (SessionId == 0 || Hash == null || Hash.Length != 32 || Status > 1)
                throw new InvalidDataException("File acknowledgement is invalid.");
            writer.Write(SessionId);
            writer.Write(Hash);
            writer.Write(Status);
        }

        public void Deserialize(BinaryReader reader)
        {
            SessionId = reader.ReadUInt64();
            Hash = reader.ReadBytes(32);
            Status = reader.ReadByte();
            if (SessionId == 0 || Hash.Length != 32 || Status > 1)
                throw new InvalidDataException("File acknowledgement is invalid.");
        }
    }
}
