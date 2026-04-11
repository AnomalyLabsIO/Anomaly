using System.IO;

namespace Anomaly.Server.Networking.Messages;

public class SilentRoleChangeMessage : IAnomalyMessage
{
    public string MessageName => "anomaly:silentrolechance";
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered; // ReliableOrdered

    public void Serialize(BinaryWriter writer)
    {
        // No payload -- flag-only message
    }

    public void Deserialize(BinaryReader reader)
    {
        // No payload
    }
}
