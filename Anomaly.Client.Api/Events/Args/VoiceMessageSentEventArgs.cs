using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class VoiceMessageSentEventArgs : VoiceMessageEventArgs
{
    public VoiceMessageSentEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }
}
