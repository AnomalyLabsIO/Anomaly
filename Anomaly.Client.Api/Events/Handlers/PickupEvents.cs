using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class PickupEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<PickupAddedEventArgs> Added;
    [ScriptEvent]
    public static event AnomalyEventHandler<PickupRemovedEventArgs> Removed;

    internal static void OnAdded(PickupAddedEventArgs args) => EventInvoker.Invoke(Added, args);

    internal static void OnRemoved(PickupRemovedEventArgs args) => EventInvoker.Invoke(Removed, args);
}
