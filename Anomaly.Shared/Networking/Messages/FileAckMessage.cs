using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class FileAckMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:fileack";
        public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

        public byte[] Hash { get; set; }
        public byte Status { get; set; } // 0 = success, 1 = failure

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Hash);
            writer.Write(Status);
        }

        public void Deserialize(BinaryReader reader)
        {
            Hash = reader.ReadBytes(32);
            Status = reader.ReadByte();
        }
    }
}
