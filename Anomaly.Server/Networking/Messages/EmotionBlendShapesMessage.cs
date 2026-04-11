using System;
using System.Collections.Generic;
using System.IO;

namespace Anomaly.Server.Networking.Messages;

public class EmotionBlendShapesMessage : IAnomalyMessage
{
    public string MessageName => "anomaly:blendshapes";
    public MessageChannel TransportChannel => MessageChannel.Sequenced; // Sequenced

    public uint NetId { get; set; }
    public List<BlendShapeEntry> BlendShapes { get; set; } = new List<BlendShapeEntry>();

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(NetId);
        writer.Write((byte)BlendShapes.Count);
        foreach (var entry in BlendShapes)
        {
            writer.Write(entry.Index);
            HalfHelper.WriteHalf(writer, entry.Weight);
        }
    }

    public void Deserialize(BinaryReader reader)
    {
        NetId = reader.ReadUInt32();
        int count = reader.ReadByte();
        BlendShapes = new List<BlendShapeEntry>(count);
        for (int i = 0; i < count; i++)
        {
            byte index = reader.ReadByte();
            float weight = HalfHelper.ReadHalf(reader);
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
