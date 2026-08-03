using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Instructs the client to destroy a previously spawned asset.
    /// </summary>
    public class AssetDespawnMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public override int MaxPayloadBytes => sizeof(uint);
        public string MessageName => "anomaly:asset:despawn";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint ObjectId { get; set; }

        public void Serialize(BinaryWriter writer) => writer.Write(ObjectId);
        public void Deserialize(BinaryReader reader) => ObjectId = reader.ReadUInt32();
    }
}
