using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class VoiceMessageReceivingEventArgs : VoiceMessageEventArgs, ICancellableEvent
{
    public VoiceMessageReceivingEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }

    public bool IsAllowed { get; set; } = true;
}
