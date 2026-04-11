using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class VoiceMessageEventArgs : EventArgs
{
    public VoiceMessageEventArgs(VoiceMessagePacket packet)
    {
        Packet = packet;
    }

    public VoiceMessagePacket Packet { get; }
}
