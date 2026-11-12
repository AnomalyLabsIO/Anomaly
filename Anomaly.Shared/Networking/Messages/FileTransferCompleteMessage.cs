using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class FileTransferCompleteMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public override int MaxPayloadBytes => 8 + 32 + 1;
        public string MessageName => "anomaly:filecomplete";
        // Completion is small control traffic, independent of chunk lanes. A
        // delayed unordered completion could fall out of the ordinary replay
        // window while the other three pipeline slots cycle through tiny files.
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public ulong SessionId { get; set; }
        public byte[] Hash { get; set; }
        public byte Attempt { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            if (SessionId == 0 || Hash == null || Hash.Length != 32 || Attempt > BulkTransferProtocol.MaxRetries)
                throw new InvalidDataException("File completion session/hash is invalid.");
            writer.Write(SessionId);
            writer.Write(Hash);
            writer.Write(Attempt);
        }

        public void Deserialize(BinaryReader reader)
        {
            SessionId = reader.ReadUInt64();
            Hash = reader.ReadBytes(32);
            Attempt = reader.ReadByte();
            if (SessionId == 0 || Hash.Length != 32 || Attempt > BulkTransferProtocol.MaxRetries)
                throw new InvalidDataException("File completion session/hash is invalid.");
        }
    }
}
