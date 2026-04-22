using Anomaly.Shared.Networking;

namespace Anomaly.Client.Api.Events.Args;

public class AnomalyMessageReceivedEventArgs : EventArgs
{
    public AnomalyMessageReceivedEventArgs(IAnomalyMessage message)
    {
        Message = message;
    }

    public IAnomalyMessage Message { get; }
}
