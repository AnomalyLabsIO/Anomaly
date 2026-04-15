using System.Collections.Generic;
using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class BoneTransformsMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:bonetransforms";
        public MessageChannel TransportChannel => MessageChannel.Sequenced; // Sequenced -- only latest pose matters

        public uint NetId { get; set; }
        public List<BoneEntry> Bones { get; set; } = new List<BoneEntry>();

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);
            writer.Write((byte)Bones.Count);
            foreach (var bone in Bones)
            {
                writer.Write(bone.Index);
                writer.Write(bone.PositionX);
                writer.Write(bone.PositionY);
                writer.Write(bone.PositionZ);
                writer.Write(bone.RotationX);
                writer.Write(bone.RotationY);
                writer.Write(bone.RotationZ);
                writer.Write(bone.RotationW);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadUInt32();
            int count = reader.ReadByte();
            Bones = new List<BoneEntry>(count);
            for (int i = 0; i < count; i++)
            {
                byte index = reader.ReadByte();
                float posX = reader.ReadSingle();
                float posY = reader.ReadSingle();
                float posZ = reader.ReadSingle();
                float rotX = reader.ReadSingle();
                float rotY = reader.ReadSingle();
                float rotZ = reader.ReadSingle();
                float rotW = reader.ReadSingle();
                Bones.Add(new BoneEntry(index, posX, posY, posZ, rotX, rotY, rotZ, rotW));
            }
        }

        /// <summary>
        /// Primitive-float bone pose entry. Shared does not reference UnityEngine, so
        /// callers convert to/from <c>Vector3</c>/<c>Quaternion</c> on their side.
        /// </summary>
        public struct BoneEntry
        {
            public byte Index;
            public float PositionX;
            public float PositionY;
            public float PositionZ;
            public float RotationX;
            public float RotationY;
            public float RotationZ;
            public float RotationW;

            public BoneEntry(byte index,
                float positionX, float positionY, float positionZ,
                float rotationX, float rotationY, float rotationZ, float rotationW)
            {
                Index = index;
                PositionX = positionX;
                PositionY = positionY;
                PositionZ = positionZ;
                RotationX = rotationX;
                RotationY = rotationY;
                RotationZ = rotationZ;
                RotationW = rotationW;
            }
        }
    }
}
