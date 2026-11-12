using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class FileAckMessage : AuthenticatedClientToServerMessage, IAnomalyMessage
    {
        public override int MaxPayloadBytes => 8 + 32 + 1 + 1;
        public string MessageName => "anomaly:fileack";
        // Keep the final file acknowledgement ordered with consent, the file
        // request, and catalog activation. The server must observe transfer
        // completion before it can accept an activation acknowledgement.
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public ulong SessionId { get; set; }
        public byte[] Hash { get; set; }
        public byte Status { get; set; } // 0 = success, 1 = failure
        public byte Attempt { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            if (SessionId == 0 || Hash == null || Hash.Length != 32 || Status > 1 || Attempt > BulkTransferProtocol.MaxRetries)
                throw new InvalidDataException("File acknowledgement is invalid.");
            writer.Write(SessionId);
            writer.Write(Hash);
            writer.Write(Status);
            writer.Write(Attempt);
        }

        public void Deserialize(BinaryReader reader)
        {
            SessionId = reader.ReadUInt64();
            Hash = reader.ReadBytes(32);
            Status = reader.ReadByte();
            Attempt = reader.ReadByte();
            if (SessionId == 0 || Hash.Length != 32 || Status > 1 || Attempt > BulkTransferProtocol.MaxRetries)
                throw new InvalidDataException("File acknowledgement is invalid.");
        }
    }
}
