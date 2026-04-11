using Anomaly.Client.API.Networking;
using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class NetworkMessageEvents
{
    public static event AnEventHandler<AnomalyMessageReceivedEventArgs> AnomalyMessageReceived;

    internal static void OnMessageReceived(IAnomalyMessage message)
    {
        EventInvoker.Invoke(AnomalyMessageReceived, new AnomalyMessageReceivedEventArgs(message));
    }
}
