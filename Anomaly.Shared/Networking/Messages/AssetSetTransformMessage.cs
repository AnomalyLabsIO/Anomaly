using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Updates one or more transform components on a spawned asset.
    /// Fields are only written/read if their bit in <see cref="DirtyMask"/> is set.
    /// </summary>
    public class AssetSetTransformMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:asset:transform";
        public MessageChannel TransportChannel => MessageChannel.Sequenced; // latest-wins

        public const byte DirtyPosition = 1 << 0;
        public const byte DirtyRotation = 1 << 1;
        public const byte DirtyScale    = 1 << 2;

        public uint ObjectId { get; set; }
        public byte DirtyMask { get; set; }

        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }

        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; }

        public float ScaleX { get; set; }
        public float ScaleY { get; set; }
        public float ScaleZ { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(ObjectId);
            writer.Write(DirtyMask);

            if ((DirtyMask & DirtyPosition) != 0)
            {
                writer.Write(PositionX); writer.Write(PositionY); writer.Write(PositionZ);
            }
            if ((DirtyMask & DirtyRotation) != 0)
            {
                writer.Write(RotationX); writer.Write(RotationY); writer.Write(RotationZ); writer.Write(RotationW);
            }
            if ((DirtyMask & DirtyScale) != 0)
            {
                writer.Write(ScaleX); writer.Write(ScaleY); writer.Write(ScaleZ);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            ObjectId = reader.ReadUInt32();
            DirtyMask = reader.ReadByte();

            if ((DirtyMask & DirtyPosition) != 0)
            {
                PositionX = reader.ReadSingle(); PositionY = reader.ReadSingle(); PositionZ = reader.ReadSingle();
            }
            if ((DirtyMask & DirtyRotation) != 0)
            {
                RotationX = reader.ReadSingle(); RotationY = reader.ReadSingle(); RotationZ = reader.ReadSingle(); RotationW = reader.ReadSingle();
            }
            if ((DirtyMask & DirtyScale) != 0)
            {
                ScaleX = reader.ReadSingle(); ScaleY = reader.ReadSingle(); ScaleZ = reader.ReadSingle();
            }
        }
    }
}
