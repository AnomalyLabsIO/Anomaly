using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local SCP-079 player is about to send a camera-switch command,
/// before <c>Scp079CurrentCameraSync.ClientSendCmd</c> runs. Cancelling keeps
/// the SCP-079 on the current camera locally; the server never receives the
/// switch request.
/// </summary>
public class Scp079SwitchingCameraEventArgs : PlayerEventArgs, ICancellableEvent
{
    public Scp079SwitchingCameraEventArgs(Player player, FacilityCamera targetCamera) : base(player)
    {
        TargetCamera = targetCamera;
    }

    /// <summary>The camera the local SCP-079 is switching to.</summary>
    public FacilityCamera TargetCamera { get; }

    public bool IsAllowed { get; set; } = true;
}
