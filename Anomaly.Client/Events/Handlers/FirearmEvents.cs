using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class FirearmEvents
{
    public static event AnomalyEventHandler<FirearmShotEventArgs> Shot;

    internal static void OnShot(FirearmShotEventArgs args) => EventInvoker.Invoke(Shot, args);
}
