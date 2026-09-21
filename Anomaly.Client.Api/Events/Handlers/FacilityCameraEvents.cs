using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// SCP-079 facility camera (CCTV) lifecycle events. Hooked from
/// <see cref="Il2CppPlayerRoles.PlayableScps.Scp079.Cameras.Scp079Camera"/>'s
/// static events.
/// </summary>
public static class FacilityCameraEvents
{
    /// <summary>Fires when a CCTV camera is created (typically at scene load).</summary>
    public static event AnomalyEventHandler<FacilityCameraEventArgs> Created;

    /// <summary>Fires when a CCTV camera is destroyed.</summary>
    public static event AnomalyEventHandler<FacilityCameraEventArgs> Removed;

    /// <summary>
    /// Fires when any CCTV camera's active/inactive state changes (e.g. SCP-079
    /// switches the active camera). Use <see cref="Anomaly.Client.Api.Features.FacilityCamera.IsActive"/> to
    /// observe the new state.
    /// </summary>
    public static event AnomalyEventHandler<FacilityCameraEventArgs> StateChanged;

    /// <summary>
    /// Fires when the local SCP-079 player is about to send a camera-switch
    /// command, before <c>Scp079CurrentCameraSync.ClientSendCmd</c> runs.
    /// Cancellable.
    /// </summary>
    public static event AnomalyEventHandler<Scp079SwitchingCameraEventArgs> Scp079Switching;

    internal static void OnCreated(FacilityCameraEventArgs args) => EventInvoker.Invoke(Created, args);
    internal static void OnRemoved(FacilityCameraEventArgs args) => EventInvoker.Invoke(Removed, args);
    internal static void OnStateChanged(FacilityCameraEventArgs args) => EventInvoker.Invoke(StateChanged, args);
    internal static void OnScp079Switching(Scp079SwitchingCameraEventArgs args) => EventInvoker.Invoke(Scp079Switching, args);
}
