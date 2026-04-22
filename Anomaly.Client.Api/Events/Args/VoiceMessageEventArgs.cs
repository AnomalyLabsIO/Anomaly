using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class VoiceMessageEventArgs : EventArgs
{
    public VoiceMessageEventArgs(VoiceMessagePacket packet)
    {
        Packet = packet;
    }

    public VoiceMessagePacket Packet { get; }
}
