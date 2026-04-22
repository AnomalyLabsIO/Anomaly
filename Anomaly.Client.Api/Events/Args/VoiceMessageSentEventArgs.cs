using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class VoiceMessageSentEventArgs : VoiceMessageEventArgs
{
    public VoiceMessageSentEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }
}
