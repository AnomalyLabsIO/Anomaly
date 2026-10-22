using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>Local scene lifecycle of hazards, not authoritative damage or player-enter events.</summary>
public static class HazardEvents
{
    public static event AnomalyEventHandler<HazardEventArgs> Added;
    public static event AnomalyEventHandler<HazardEventArgs> Removed;

    internal static void OnAdded(HazardEventArgs args) => EventInvoker.Invoke(Added, args);
    internal static void OnRemoved(HazardEventArgs args) => EventInvoker.Invoke(Removed, args);
}
