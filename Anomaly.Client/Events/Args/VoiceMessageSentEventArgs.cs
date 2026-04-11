using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class VoiceMessageSentEventArgs : VoiceMessageEventArgs
{
    public VoiceMessageSentEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }
}
