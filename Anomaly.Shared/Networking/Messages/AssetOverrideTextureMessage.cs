using System.IO;
using System.Text;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Applies a texture override to an <b>existing</b> networked
    /// object (player, room, door, prop) without spawning anything new. The target
    /// is addressed by <see cref="NetId"/> + a Transform-name path to the target
    /// <c>Renderer</c>. An empty <see cref="ComponentPath"/> means "the first
    /// <c>Renderer</c> found via depth-first walk".
    /// </summary>
    public class AssetOverrideTextureMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:asset:override-texture";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint NetId { get; set; }
        public string ComponentPath { get; set; } = "";
        public ushort MaterialIndex { get; set; }
        public byte[] TextureHash { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);

            var pathBytes = Encoding.UTF8.GetBytes(ComponentPath ?? "");
            writer.Write((ushort)pathBytes.Length);
            writer.Write(pathBytes);

            writer.Write(MaterialIndex);
            writer.Write(TextureHash);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadUInt32();

            int pathLen = reader.ReadUInt16();
            ComponentPath = Encoding.UTF8.GetString(reader.ReadBytes(pathLen));

            MaterialIndex = reader.ReadUInt16();
            TextureHash = reader.ReadBytes(32);
        }
    }
}
