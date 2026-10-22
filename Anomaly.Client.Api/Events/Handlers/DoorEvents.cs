using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>Client-observed door lifecycle and replicated state. These events cannot veto server changes.</summary>
public static class DoorEvents
{
    public static event AnomalyEventHandler<DoorEventArgs> Added;
    public static event AnomalyEventHandler<DoorEventArgs> Removed;
    public static event AnomalyEventHandler<DoorStateChangedEventArgs> StateChanged;
    public static event AnomalyEventHandler<DoorLocksChangedEventArgs> LocksChanged;

    internal static void OnAdded(DoorEventArgs args) => EventInvoker.Invoke(Added, args);
    internal static void OnRemoved(DoorEventArgs args) => EventInvoker.Invoke(Removed, args);
    internal static void OnStateChanged(DoorStateChangedEventArgs args) => EventInvoker.Invoke(StateChanged, args);
    internal static void OnLocksChanged(DoorLocksChangedEventArgs args) => EventInvoker.Invoke(LocksChanged, args);
}
