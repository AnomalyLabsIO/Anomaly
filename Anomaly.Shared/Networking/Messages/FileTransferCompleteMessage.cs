using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class FileTransferCompleteMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public override int MaxPayloadBytes => 8 + 32;
        public string MessageName => "anomaly:filecomplete";
        public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

        public ulong SessionId { get; set; }
        public byte[] Hash { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            if (SessionId == 0 || Hash == null || Hash.Length != 32)
                throw new InvalidDataException("File completion session/hash is invalid.");
            writer.Write(SessionId);
            writer.Write(Hash);
        }

        public void Deserialize(BinaryReader reader)
        {
            SessionId = reader.ReadUInt64();
            Hash = reader.ReadBytes(32);
            if (SessionId == 0 || Hash.Length != 32)
                throw new InvalidDataException("File completion session/hash is invalid.");
        }
    }
}
