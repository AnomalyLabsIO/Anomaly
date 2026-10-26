using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// Local camera-effect events. Cancellation drops the effect on the floor;
/// nothing about the server or other clients changes.
/// </summary>
public static class CameraEvents
{
    /// <summary>Fires before a shake effect is enqueued on the local camera. Cancel to skip the shake.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<CameraShakingEventArgs> Shaking;

    internal static void OnShaking(CameraShakingEventArgs args) => EventInvoker.Invoke(Shaking, args);
}
