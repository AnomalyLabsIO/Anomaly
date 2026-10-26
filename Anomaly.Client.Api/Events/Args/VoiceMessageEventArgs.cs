using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class VoiceMessageEventArgs : EventArgs
{
    public VoiceMessageEventArgs(VoiceMessagePacket packet)
    {
        Packet = packet;
    }

    [ScriptVisible]
    public VoiceMessagePacket Packet { get; }
}
