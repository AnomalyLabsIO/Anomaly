using Anomaly.Client.Api.Events.Args;
using Anomaly.Shared.Networking;

namespace Anomaly.Client.Api.Events.Handlers;

public static class NetworkMessageEvents
{
    public static event AnomalyEventHandler<AnomalyMessageReceivedEventArgs> AnomalyMessageReceived;

    internal static void OnMessageReceived(IAnomalyMessage message)
    {
        EventInvoker.Invoke(AnomalyMessageReceived, new AnomalyMessageReceivedEventArgs(message));
    }
}
