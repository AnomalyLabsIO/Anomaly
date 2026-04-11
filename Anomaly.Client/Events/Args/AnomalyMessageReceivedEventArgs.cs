using Anomaly.Client.API.Networking;

namespace Anomaly.Client.Events.Args;

public class AnomalyMessageReceivedEventArgs : EventArgs
{
    public AnomalyMessageReceivedEventArgs(IAnomalyMessage message)
    {
        Message = message;
    }

    public IAnomalyMessage Message { get; }
}
