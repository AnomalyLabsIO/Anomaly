using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class PickupEvents
{
    public static event AnEventHandler<PickupAddedEventArgs> Added;
    public static event AnEventHandler<PickupRemovedEventArgs> Removed;

    internal static void OnAdded(PickupAddedEventArgs args) => EventInvoker.Invoke(Added, args);

    internal static void OnRemoved(PickupRemovedEventArgs args) => EventInvoker.Invoke(Removed, args);
}
