using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class VoiceMessageSendingEventArgs : VoiceMessageEventArgs, ICancellableEvent
{
    public VoiceMessageSendingEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }

    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
