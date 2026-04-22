using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class FirearmEvents
{
    public static event AnomalyEventHandler<FirearmShotEventArgs> Shot;

    internal static void OnShot(FirearmShotEventArgs args) => EventInvoker.Invoke(Shot, args);
}
