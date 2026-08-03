using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Replaces the main texture on a specific renderer/material
    /// slot of a spawned asset.
    /// </summary>
    public class AssetSetTextureMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public override int MaxPayloadBytes => 40;
        public string MessageName => "anomaly:asset:texture";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint ObjectId { get; set; }
        public ushort RendererIndex { get; set; }
        public ushort MaterialIndex { get; set; }
        public byte[] TextureHash { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            if (TextureHash == null || TextureHash.Length != 32)
                throw new InvalidDataException("Texture hash must be 32 bytes.");
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
            if (TextureHash.Length != 32)
                throw new EndOfStreamException("Texture hash was truncated.");
        }
    }
}
