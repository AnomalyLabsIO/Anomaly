using System.IO;
using Anomaly.Shared.Assets;
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
    public class AssetOverrideTextureMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private const int MaxComponentPathBytes = TextureOverridePolicy.MaximumComponentPathBytes;
        public override int MaxPayloadBytes => 4 + 2 + MaxComponentPathBytes + 2 + 2 + AssetAddress.MaxLength;
        public string MessageName => "anomaly:asset:override-texture";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint NetId { get; set; }
        public string ComponentPath { get; set; } = "";
        public ushort MaterialIndex { get; set; }
        public string TextureAddress { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            TextureOverridePolicy.ValidateTarget(NetId, ComponentPath, MaterialIndex);
            writer.Write(NetId);

            var pathBytes = Utf8.GetBytes(ComponentPath ?? "");
            if (pathBytes.Length > MaxComponentPathBytes)
                throw new InvalidDataException($"Component path exceeded {MaxComponentPathBytes} bytes.");
            writer.Write((ushort)pathBytes.Length);
            writer.Write(pathBytes);

            writer.Write(MaterialIndex);
            AssetAddressWire.Write(writer, TextureAddress);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadUInt32();

            int pathLen = reader.ReadUInt16();
            if (pathLen > MaxComponentPathBytes)
                throw new InvalidDataException($"Component path exceeded {MaxComponentPathBytes} bytes.");
            byte[] pathBytes = reader.ReadBytes(pathLen);
            if (pathBytes.Length != pathLen)
                throw new EndOfStreamException("Component path was truncated.");
            ComponentPath = Utf8.GetString(pathBytes);

            MaterialIndex = reader.ReadUInt16();
            TextureOverridePolicy.ValidateTarget(NetId, ComponentPath, MaterialIndex);
            TextureAddress = AssetAddressWire.Read(reader);
        }
    }
}
