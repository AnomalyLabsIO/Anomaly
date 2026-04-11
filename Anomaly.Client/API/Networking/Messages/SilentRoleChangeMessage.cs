using Anomaly.Client.API.Enum;
using Anomaly.Client.Internal;

namespace Anomaly.Client.API.Networking.Messages;

public class SilentRoleChangeMessage : IAnomalyMessage
{
    public string MessageName => "anomaly:silentrolechance";
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered; // ReliableOrdered -- must arrive before RoleSyncInfo

    public void Serialize(BinaryWriter writer)
    {
        // No payload -- this is a flag-only message
    }

    public void Deserialize(BinaryReader reader)
    {
        // No payload
    }

    internal static void Register()
    {
        AnomalyMessaging.MessageReceived += msg =>
        {
            if (msg is SilentRoleChangeMessage)
                SilentRoleChange.IsPending = true;
        };
    }
}
