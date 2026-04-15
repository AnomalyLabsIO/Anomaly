using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Reverts every active texture override targeting the
    /// given <see cref="NetId"/>.
    /// </summary>
    public class AssetClearAllOverridesMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:asset:override-clearall";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint NetId { get; set; }

        public void Serialize(BinaryWriter writer) => writer.Write(NetId);
        public void Deserialize(BinaryReader reader) => NetId = reader.ReadUInt32();
    }
}
