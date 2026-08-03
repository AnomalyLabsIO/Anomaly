using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server-to-client challenge that establishes the server-owned round epoch
    /// before a capability snapshot is accepted.
    /// </summary>
    public sealed class CapabilityChallengeMessage :
        AuthenticatedServerToClientMessage,
        IAnomalyMessage
    {
        public string MessageName => "anomaly:capability:challenge";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public override int MaxPayloadBytes => 8;

        public uint RoundEpoch { get; set; }
        public uint Sequence { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(RoundEpoch);
            writer.Write(Sequence);
        }

        public void Deserialize(BinaryReader reader)
        {
            RoundEpoch = reader.ReadUInt32();
            Sequence = reader.ReadUInt32();
        }
    }
}
