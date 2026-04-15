using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Signals that the asset previously known by <see cref="OldHash"/>
    /// now has new content identified by <see cref="NewHash"/>. The client is expected to
    /// swap all references (spawns using it, materials using it) to the new hash.
    /// </summary>
    public class AssetReloadMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:asset:reload";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public byte[] OldHash { get; set; }
        public byte[] NewHash { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(OldHash);
            writer.Write(NewHash);
        }

        public void Deserialize(BinaryReader reader)
        {
            OldHash = reader.ReadBytes(32);
            NewHash = reader.ReadBytes(32);
        }
    }
}
