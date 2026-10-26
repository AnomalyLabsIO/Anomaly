using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Assets;

/// <summary>
/// Mod-facing events raised by the asset pipeline.
/// </summary>
public static class AssetEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<AssetOverrideAppliedEventArgs> OverrideApplied;
    [ScriptEvent]
    public static event AnomalyEventHandler<AssetOverrideClearedEventArgs> OverrideCleared;

    internal static void OnOverrideApplied(AssetOverrideAppliedEventArgs args) => EventInvoker.Invoke(OverrideApplied, args);
    internal static void OnOverrideCleared(AssetOverrideClearedEventArgs args) => EventInvoker.Invoke(OverrideCleared, args);
}
