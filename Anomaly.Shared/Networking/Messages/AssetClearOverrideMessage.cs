using System.IO;
using Anomaly.Shared.Assets;
using System.Text;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Reverts a specific texture override, restoring the
    /// original material texture the client cached when the override was first
    /// applied.
    /// </summary>
    public class AssetClearOverrideMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private const int MaxComponentPathBytes = TextureOverridePolicy.MaximumComponentPathBytes;
        public override int MaxPayloadBytes => 4 + 2 + MaxComponentPathBytes + 2;
        public string MessageName => "anomaly:asset:override-clear";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint NetId { get; set; }
        public string ComponentPath { get; set; } = "";
        public ushort MaterialIndex { get; set; }

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
        }
    }
}
