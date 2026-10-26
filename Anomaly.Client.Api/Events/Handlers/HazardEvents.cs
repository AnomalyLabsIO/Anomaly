using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>Local scene lifecycle of hazards, not authoritative damage or player-enter events.</summary>
public static class HazardEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<HazardEventArgs> Added;
    [ScriptEvent]
    public static event AnomalyEventHandler<HazardEventArgs> Removed;

    internal static void OnAdded(HazardEventArgs args) => EventInvoker.Invoke(Added, args);
    internal static void OnRemoved(HazardEventArgs args) => EventInvoker.Invoke(Removed, args);
}
