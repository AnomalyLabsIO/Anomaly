using System.Collections.Generic;
using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class FileRequestMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:filerequest";
        public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

        public List<byte[]> RequestedHashes { get; set; } = new List<byte[]>();

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((ushort)RequestedHashes.Count);
            foreach (var hash in RequestedHashes)
                writer.Write(hash);
        }

        public void Deserialize(BinaryReader reader)
        {
            int count = reader.ReadUInt16();
            RequestedHashes = new List<byte[]>(count);
            for (int i = 0; i < count; i++)
                RequestedHashes.Add(reader.ReadBytes(32));
        }
    }
}
