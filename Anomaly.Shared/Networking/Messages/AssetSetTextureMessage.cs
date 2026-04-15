using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Replaces the main texture on a specific renderer/material
    /// slot of a spawned asset.
    /// </summary>
    public class AssetSetTextureMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:asset:texture";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint ObjectId { get; set; }
        public ushort RendererIndex { get; set; }
        public ushort MaterialIndex { get; set; }
        public byte[] TextureHash { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(ObjectId);
            writer.Write(RendererIndex);
            writer.Write(MaterialIndex);
            writer.Write(TextureHash);
        }

        public void Deserialize(BinaryReader reader)
        {
            ObjectId = reader.ReadUInt32();
            RendererIndex = reader.ReadUInt16();
            MaterialIndex = reader.ReadUInt16();
            TextureHash = reader.ReadBytes(32);
        }
    }
}
