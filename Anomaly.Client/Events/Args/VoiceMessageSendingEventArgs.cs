using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class VoiceMessageSendingEventArgs : VoiceMessageEventArgs
{
    public VoiceMessageSendingEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }
}
