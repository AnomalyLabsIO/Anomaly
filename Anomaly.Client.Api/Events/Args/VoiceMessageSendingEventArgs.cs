using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class VoiceMessageSendingEventArgs : VoiceMessageEventArgs
{
    public VoiceMessageSendingEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }
}
