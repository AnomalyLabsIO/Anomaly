using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Payload-less flag notifying the receiver that a role change has happened silently.
    /// The side-specific handler hook (e.g. setting a pending flag on the client) lives
    /// outside this class.
    /// </summary>
    public class SilentRoleChangeMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:silentrolechange";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered; // ReliableOrdered -- must arrive before RoleSyncInfo

        public void Serialize(BinaryWriter writer)
        {
            // No payload -- flag-only message
        }

        public void Deserialize(BinaryReader reader)
        {
            // No payload
        }
    }
}
