using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class VoiceMessageReceivingEventArgs : VoiceMessageEventArgs, ICancellableEvent
{
    public VoiceMessageReceivingEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }

    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
