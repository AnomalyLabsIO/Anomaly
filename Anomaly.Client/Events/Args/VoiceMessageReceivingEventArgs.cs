using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class VoiceMessageReceivingEventArgs : VoiceMessageEventArgs
{
    public VoiceMessageReceivingEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }
}
