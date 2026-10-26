using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class VoiceMessageReceivedEventArgs : VoiceMessageEventArgs
{
    public VoiceMessageReceivedEventArgs(VoiceMessagePacket packet) : base(packet)
    {
    }
}
