using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Assets;

/// <summary>
/// Mod-facing events raised by the asset pipeline.
/// </summary>
public static class AssetEvents
{
    public static event AnomalyEventHandler<AssetOverrideAppliedEventArgs> OverrideApplied;
    public static event AnomalyEventHandler<AssetOverrideClearedEventArgs> OverrideCleared;

    internal static void OnOverrideApplied(AssetOverrideAppliedEventArgs args) => EventInvoker.Invoke(OverrideApplied, args);
    internal static void OnOverrideCleared(AssetOverrideClearedEventArgs args) => EventInvoker.Invoke(OverrideCleared, args);
}
