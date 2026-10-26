using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local SCP-079 player is about to send a camera-switch command,
/// before <c>Scp079CurrentCameraSync.ClientSendCmd</c> runs. Cancelling keeps
/// the SCP-079 on the current camera locally; the server never receives the
/// switch request.
/// </summary>
[ScriptType]
public class Scp079SwitchingCameraEventArgs : PlayerEventArgs, ICancellableEvent
{
    public Scp079SwitchingCameraEventArgs(Player player, FacilityCamera targetCamera) : base(player)
    {
        TargetCamera = targetCamera;
    }

    /// <summary>The camera the local SCP-079 is switching to.</summary>
    [ScriptVisible]
    public FacilityCamera TargetCamera { get; }

    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
