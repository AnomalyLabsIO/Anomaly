using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class VoiceMessageReceivingEventArgs : VoiceMessageEventArgs
{
    public VoiceMessageReceivingEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }
}
