using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Toggles <c>GameObject.SetActive</c> on a spawned asset.
    /// </summary>
    public class AssetSetActiveMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:asset:active";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint ObjectId { get; set; }
        public bool Active { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(ObjectId);
            writer.Write(Active);
        }

        public void Deserialize(BinaryReader reader)
        {
            ObjectId = reader.ReadUInt32();
            Active = reader.ReadBoolean();
        }
    }
}
