using Anomaly.Shared.Networking;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class AnomalyMessageReceivedEventArgs : EventArgs
{
    public AnomalyMessageReceivedEventArgs(IAnomalyMessage message)
    {
        Message = message;
    }

    public IAnomalyMessage Message { get; }
}
