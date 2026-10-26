using Anomaly.Client.Api.Events.Args;
using Anomaly.Shared.Networking;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class NetworkMessageEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<AnomalyMessageReceivedEventArgs> AnomalyMessageReceived;

    internal static void OnMessageReceived(IAnomalyMessage message)
    {
        EventInvoker.Invoke(AnomalyMessageReceived, new AnomalyMessageReceivedEventArgs(message));
    }
}
