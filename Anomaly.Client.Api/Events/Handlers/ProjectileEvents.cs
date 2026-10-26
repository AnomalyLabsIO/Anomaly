using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class ProjectileEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<ProjectileSpawnedEventArgs> Spawned;
    [ScriptEvent]
    public static event AnomalyEventHandler<ProjectileRemovedEventArgs> Removed;

    /// <summary>Fires when the local player initiates a throw, before the begin-throw network message is sent. Cancellable.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<PlayerThrowingEventArgs> Throwing;

    internal static void OnSpawned(ProjectileSpawnedEventArgs args) => EventInvoker.Invoke(Spawned, args);
    internal static void OnRemoved(ProjectileRemovedEventArgs args) => EventInvoker.Invoke(Removed, args);
    internal static void OnThrowing(PlayerThrowingEventArgs args) => EventInvoker.Invoke(Throwing, args);
}
