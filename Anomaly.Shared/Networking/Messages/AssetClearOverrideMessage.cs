using System.IO;
using System.Text;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Reverts a specific texture override, restoring the
    /// original material texture the client cached when the override was first
    /// applied.
    /// </summary>
    public class AssetClearOverrideMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:asset:override-clear";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint NetId { get; set; }
        public string ComponentPath { get; set; } = "";
        public ushort MaterialIndex { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);

            var pathBytes = Encoding.UTF8.GetBytes(ComponentPath ?? "");
            writer.Write((ushort)pathBytes.Length);
            writer.Write(pathBytes);

            writer.Write(MaterialIndex);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadUInt32();

            int pathLen = reader.ReadUInt16();
            ComponentPath = Encoding.UTF8.GetString(reader.ReadBytes(pathLen));

            MaterialIndex = reader.ReadUInt16();
        }
    }
}
