using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class PlayerEvents
{
    public static event AnomalyEventHandler<PlayerAddedEventArgs> Added;
    public static event AnomalyEventHandler<PlayerRemovedEventArgs> Removed;

    internal static void OnAdded(PlayerAddedEventArgs args) => EventInvoker.Invoke(Added, args);

    internal static void OnRemoved(PlayerRemovedEventArgs args) => EventInvoker.Invoke(Removed, args);
}
