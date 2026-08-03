using System.Collections.Generic;
using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class EmotionBlendShapesMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public const int MaxBlendShapes = 128;
        public override int MaxPayloadBytes => 4 + 1 + (MaxBlendShapes * 3);
        public string MessageName => "anomaly:blendshapes";
        public MessageChannel TransportChannel => MessageChannel.Sequenced; // Sequenced -- only latest state matters

        public uint NetId { get; set; }
        public List<BlendShapeEntry> BlendShapes { get; set; } = new List<BlendShapeEntry>();

        public void Serialize(BinaryWriter writer)
        {
            if (BlendShapes == null || BlendShapes.Count > MaxBlendShapes)
                throw new InvalidDataException($"Blend-shape count exceeded {MaxBlendShapes}.");
            writer.Write(NetId);
            writer.Write((byte)BlendShapes.Count);
            foreach (var entry in BlendShapes)
            {
                if (float.IsNaN(entry.Weight) || float.IsInfinity(entry.Weight))
                    throw new InvalidDataException("Blend-shape weight was non-finite.");
                writer.Write(entry.Index);
                HalfHelper.WriteHalf(writer, entry.Weight);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadUInt32();
            int count = reader.ReadByte();
            if (count > MaxBlendShapes)
                throw new InvalidDataException($"Blend-shape count exceeded {MaxBlendShapes}.");
            BlendShapes = new List<BlendShapeEntry>(count);
            for (int i = 0; i < count; i++)
            {
                byte index = reader.ReadByte();
                float weight = HalfHelper.ReadHalf(reader);
                if (float.IsNaN(weight) || float.IsInfinity(weight))
                    throw new InvalidDataException("Blend-shape weight was non-finite.");
                BlendShapes.Add(new BlendShapeEntry(index, weight));
            }
        }

        public struct BlendShapeEntry
        {
            public byte Index;
            public float Weight;

            public BlendShapeEntry(byte index, float weight)
            {
                Index = index;
                Weight = weight;
            }
        }
    }
}
