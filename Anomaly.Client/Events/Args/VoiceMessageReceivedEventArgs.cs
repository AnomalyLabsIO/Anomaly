using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class VoiceMessageReceivedEventArgs : VoiceMessageEventArgs
{
    public VoiceMessageReceivedEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }
}
