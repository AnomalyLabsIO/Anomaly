using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class PickupEvents
{
    public static event AnomalyEventHandler<PickupAddedEventArgs> Added;
    public static event AnomalyEventHandler<PickupRemovedEventArgs> Removed;

    internal static void OnAdded(PickupAddedEventArgs args) => EventInvoker.Invoke(Added, args);

    internal static void OnRemoved(PickupRemovedEventArgs args) => EventInvoker.Invoke(Removed, args);
}
