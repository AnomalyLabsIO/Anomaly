using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Assets;

namespace Anomaly.Shared.Networking.Messages;

public sealed class DecorativeVisualSnapshotMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
{
    public override int MaxPayloadBytes => 64 * 1024;
    public string MessageName => "anomaly:visual:snapshot";
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public ulong Generation { get; set; }
    public ulong Revision { get; set; }
    public List<DecorativeVisualInstance> Instances { get; set; } = new List<DecorativeVisualInstance>();
    public void Serialize(BinaryWriter writer)
    {
        if (Generation == 0 || Instances == null || Instances.Count > AssetPackIndexCodec.MaxSceneInstances)
            throw new InvalidDataException("Invalid visual snapshot generation/count.");
        var ids = new HashSet<uint>();
        foreach (DecorativeVisualInstance instance in Instances)
        {
            if (instance == null || !ids.Add(instance.Id)) throw new InvalidDataException("Duplicate/null visual instance.");
            instance.Validate();
        }
        writer.Write(Generation); writer.Write(Revision); writer.Write((ushort)Instances.Count);
        foreach (DecorativeVisualInstance instance in Instances) instance.Write(writer);
    }
    public void Deserialize(BinaryReader reader)
    {
        Generation = reader.ReadUInt64(); Revision = reader.ReadUInt64(); int count = reader.ReadUInt16();
        if (Generation == 0 || count > AssetPackIndexCodec.MaxSceneInstances) throw new InvalidDataException("Invalid visual snapshot.");
        Instances = new List<DecorativeVisualInstance>(count);
        var ids = new HashSet<uint>();
        for (int i = 0; i < count; i++)
        {
            DecorativeVisualInstance instance = DecorativeVisualInstance.Read(reader);
            if (!ids.Add(instance.Id)) throw new InvalidDataException("Duplicate visual instance.");
            Instances.Add(instance);
        }
    }
}
