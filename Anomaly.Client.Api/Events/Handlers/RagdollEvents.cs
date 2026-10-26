using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// Ragdoll lifecycle events. Driven by Harmony patches on
/// <see cref="Il2CppPlayerRoles.Ragdolls.BasicRagdoll"/>'s <c>Start</c> and
/// <c>OnDestroy</c> methods (the engine doesn't expose static lifecycle events
/// for ragdolls, so the loader uses patches instead).
/// </summary>
public static class RagdollEvents
{
    /// <summary>Fires when a new ragdoll spawns into the scene (someone died).</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<RagdollEventArgs> Spawned;

    /// <summary>Fires when a ragdoll is removed from the scene (cleanup, round restart).</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<RagdollEventArgs> Removed;

    internal static void OnSpawned(RagdollEventArgs args) => EventInvoker.Invoke(Spawned, args);
    internal static void OnRemoved(RagdollEventArgs args) => EventInvoker.Invoke(Removed, args);
}
