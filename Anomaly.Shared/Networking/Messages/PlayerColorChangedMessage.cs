using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server -> client broadcast. Sent to every connected client whenever a
    /// player's stored favorite color changes, and replayed once per known
    /// player to a new joiner on encrypted-channel ready (so they paint the
    /// correct jumpsuit tints from the first frame).
    /// </summary>
    /// <remarks>
    /// Uses <see cref="MessageChannel.ReliableOrdered"/> rather than
    /// <see cref="MessageChannel.Sequenced"/> because snapshot-on-join sends
    /// many of these back-to-back; Sequenced would discard all but the most
    /// recent and the new joiner would only see one player's color.
    /// </remarks>
    public class PlayerColorChangedMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:player:colorchanged";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint NetId { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);
            writer.Write(R);
            writer.Write(G);
            writer.Write(B);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadUInt32();
            R = reader.ReadByte();
            G = reader.ReadByte();
            B = reader.ReadByte();
        }
    }
}
