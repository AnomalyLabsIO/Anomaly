using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Assets;

/// <summary>
/// Mod-facing events raised by the asset pipeline.
/// </summary>
public static class AssetEvents
{
    public static event AnomalyEventHandler<AssetSpawnedEventArgs> Spawned;
    public static event AnomalyEventHandler<AssetDespawnedEventArgs> Despawned;
    public static event AnomalyEventHandler<AssetReloadedEventArgs> Reloaded;
    public static event AnomalyEventHandler<AssetOverrideAppliedEventArgs> OverrideApplied;
    public static event AnomalyEventHandler<AssetOverrideClearedEventArgs> OverrideCleared;

    internal static void OnSpawned(AssetSpawnedEventArgs args) => EventInvoker.Invoke(Spawned, args);
    internal static void OnDespawned(AssetDespawnedEventArgs args) => EventInvoker.Invoke(Despawned, args);
    internal static void OnReloaded(AssetReloadedEventArgs args) => EventInvoker.Invoke(Reloaded, args);
    internal static void OnOverrideApplied(AssetOverrideAppliedEventArgs args) => EventInvoker.Invoke(OverrideApplied, args);
    internal static void OnOverrideCleared(AssetOverrideClearedEventArgs args) => EventInvoker.Invoke(OverrideCleared, args);
}
