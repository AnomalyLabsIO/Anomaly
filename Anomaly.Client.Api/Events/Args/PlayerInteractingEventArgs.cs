using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local player presses the interact key, before the raycast
/// and <c>CmdServerInteract</c> RPC run. Cancelling skips both the local
/// <c>IClientInteractable.ClientInteract</c> callback and the server command.
/// </summary>
[ScriptType]
public class PlayerInteractingEventArgs : PlayerEventArgs, ICancellableEvent
{
    public PlayerInteractingEventArgs(Player player) : base(player)
    {
    }
    
    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
