using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class ProjectileEvents
{
    public static event AnomalyEventHandler<ProjectileSpawnedEventArgs> Spawned;
    public static event AnomalyEventHandler<ProjectileRemovedEventArgs> Removed;

    internal static void OnSpawned(ProjectileSpawnedEventArgs args) => EventInvoker.Invoke(Spawned, args);

    internal static void OnRemoved(ProjectileRemovedEventArgs args) => EventInvoker.Invoke(Removed, args);
}
