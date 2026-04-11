namespace Anomaly.Client.API.Networking.Messages;

public class EmotionBlendShapesMessage : IAnomalyMessage
{
    public string MessageName => "anomaly:blendshapes";
    public int TransportChannel => 2; // Sequenced -- only latest state matters

    public uint NetId { get; set; }
    public List<BlendShapeEntry> BlendShapes { get; set; } = new();

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(NetId);
        writer.Write((byte)BlendShapes.Count);
        foreach (var entry in BlendShapes)
        {
            writer.Write(entry.Index);
            writer.Write((Half)entry.Weight);
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
            float weight = (float)reader.ReadHalf();
            BlendShapes.Add(new BlendShapeEntry(index, weight));
        }
    }

    public readonly record struct BlendShapeEntry(byte Index, float Weight);
}
