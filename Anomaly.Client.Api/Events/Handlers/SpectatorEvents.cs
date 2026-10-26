using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>Events for the local client's spectator camera.</summary>
public static class SpectatorEvents
{
    /// <summary>
    /// Fires after the stock tracker changes its player target, including when
    /// the target is cleared. This does not observe remote spectators.
    /// </summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<SpectatedPlayerChangedEventArgs> TargetChanged;

    internal static void OnTargetChanged(SpectatedPlayerChangedEventArgs args) => EventInvoker.Invoke(TargetChanged, args);
}
