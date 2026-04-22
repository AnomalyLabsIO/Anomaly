using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class VoiceMessageReceivedEventArgs : VoiceMessageEventArgs
{
    public VoiceMessageReceivedEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }
}
