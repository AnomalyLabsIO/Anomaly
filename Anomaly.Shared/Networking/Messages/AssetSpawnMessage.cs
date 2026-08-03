using System.IO;
using System.Text;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Instructs the client to instantiate an asset and register it
    /// under the server-assigned <see cref="ObjectId"/>.
    /// </summary>
    public class AssetSpawnMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public override int MaxPayloadBytes => 332;
        public string MessageName => "anomaly:asset:spawn";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint ObjectId { get; set; }
        public byte[] AssetHash { get; set; }
        public string BundleAssetName { get; set; } = "";

        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }

        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; }

        public float ScaleX { get; set; } = 1f;
        public float ScaleY { get; set; } = 1f;
        public float ScaleZ { get; set; } = 1f;

        public void Serialize(BinaryWriter writer)
        {
            Validate();
            writer.Write(ObjectId);
            writer.Write(AssetHash);

            var nameBytes = Encoding.UTF8.GetBytes(BundleAssetName ?? "");
            writer.Write((byte)nameBytes.Length);
            writer.Write(nameBytes);

            writer.Write(PositionX); writer.Write(PositionY); writer.Write(PositionZ);
            writer.Write(RotationX); writer.Write(RotationY); writer.Write(RotationZ); writer.Write(RotationW);
            writer.Write(ScaleX); writer.Write(ScaleY); writer.Write(ScaleZ);
        }

        public void Deserialize(BinaryReader reader)
        {
            ObjectId = reader.ReadUInt32();
            AssetHash = reader.ReadBytes(32);
            if (AssetHash.Length != 32)
                throw new EndOfStreamException("Asset spawn hash was truncated.");

            int nameLen = reader.ReadByte();
            byte[] nameBytes = reader.ReadBytes(nameLen);
            if (nameLen == 0 || nameBytes.Length != nameLen)
                throw new InvalidDataException("Asset spawn name was empty or truncated.");
            BundleAssetName = Encoding.UTF8.GetString(nameBytes);

            PositionX = reader.ReadSingle(); PositionY = reader.ReadSingle(); PositionZ = reader.ReadSingle();
            RotationX = reader.ReadSingle(); RotationY = reader.ReadSingle(); RotationZ = reader.ReadSingle(); RotationW = reader.ReadSingle();
            ScaleX = reader.ReadSingle(); ScaleY = reader.ReadSingle(); ScaleZ = reader.ReadSingle();
            Validate();
        }

        private void Validate()
        {
            if (AssetHash == null || AssetHash.Length != 32)
                throw new InvalidDataException("Asset spawn hash must be 32 bytes.");
            int nameLength = Encoding.UTF8.GetByteCount(BundleAssetName ?? string.Empty);
            if (nameLength == 0 || nameLength > byte.MaxValue)
                throw new InvalidDataException("Asset spawn name was empty or too long.");
            if (!IsFinite(PositionX) || !IsFinite(PositionY) || !IsFinite(PositionZ) ||
                !IsFinite(RotationX) || !IsFinite(RotationY) || !IsFinite(RotationZ) || !IsFinite(RotationW) ||
                !IsFinite(ScaleX) || !IsFinite(ScaleY) || !IsFinite(ScaleZ))
                throw new InvalidDataException("Asset spawn transform contained a non-finite value.");
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
