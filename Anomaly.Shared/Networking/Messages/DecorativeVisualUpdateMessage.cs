using System.IO;
using Anomaly.Shared.Assets;

namespace Anomaly.Shared.Networking.Messages;

public sealed class DecorativeVisualUpdateMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
{
    public override int MaxPayloadBytes => 320;
    public string MessageName => "anomaly:visual:update";
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public ulong Generation { get; set; }
    public ulong Revision { get; set; }
    public DecorativeVisualOperation Operation { get; set; }
    public DecorativeVisualInstance Instance { get; set; }
    public void Serialize(BinaryWriter writer)
    {
        ValidateHeader();
        writer.Write(Generation); writer.Write(Revision); writer.Write((byte)Operation);
        if (Operation == DecorativeVisualOperation.Remove) writer.Write(Instance.Id);
        else Instance.Write(writer);
    }
    public void Deserialize(BinaryReader reader)
    {
        Generation = reader.ReadUInt64(); Revision = reader.ReadUInt64(); Operation = (DecorativeVisualOperation)reader.ReadByte();
        if (Operation != DecorativeVisualOperation.Remove && Operation != DecorativeVisualOperation.Upsert)
            throw new InvalidDataException("Unknown visual operation.");
        Instance = Operation == DecorativeVisualOperation.Remove
            ? new DecorativeVisualInstance { Id = reader.ReadUInt32() } : DecorativeVisualInstance.Read(reader);
        ValidateHeader();
    }
    private void ValidateHeader()
    {
        if (Generation == 0 || Revision == 0 || Instance == null || Instance.Id == 0 ||
            (Operation != DecorativeVisualOperation.Remove && Operation != DecorativeVisualOperation.Upsert))
            throw new InvalidDataException("Invalid visual update.");
        if (Operation == DecorativeVisualOperation.Upsert) Instance.Validate();
    }
}
