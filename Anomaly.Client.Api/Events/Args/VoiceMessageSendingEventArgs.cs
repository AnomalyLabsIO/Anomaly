using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class VoiceMessageSendingEventArgs : VoiceMessageEventArgs, ICancellableEvent
{
    public VoiceMessageSendingEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }

    public bool IsAllowed { get; set; } = true;
}
