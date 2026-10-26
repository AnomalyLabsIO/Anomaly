using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// Tesla gate lifecycle events. Hooked from <see cref="Il2Cpp.TeslaGate"/>'s
/// static <c>OnAdded</c> / <c>OnRemoved</c> / <c>OnBursted</c> events.
/// </summary>
public static class TeslaEvents
{
    /// <summary>Fires when a tesla gate spawns into the scene.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<TeslaEventArgs> Added;

    /// <summary>Fires when a tesla gate is removed from the scene.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<TeslaEventArgs> Removed;

    /// <summary>Fires when a tesla gate completes its shock burst.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<TeslaEventArgs> Bursted;

    internal static void OnAdded(TeslaEventArgs args) => EventInvoker.Invoke(Added, args);
    internal static void OnRemoved(TeslaEventArgs args) => EventInvoker.Invoke(Removed, args);
    internal static void OnBursted(TeslaEventArgs args) => EventInvoker.Invoke(Bursted, args);
}
