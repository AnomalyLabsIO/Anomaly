using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local player transitions into the sprinting movement state.
/// Cancelling keeps the movement state at walking; the server only observes
/// movement state changes, so suppressing this is desync-safe.
/// </summary>
[ScriptType]
public class PlayerSprintingEventArgs : PlayerEventArgs, ICancellableEvent
{
    public PlayerSprintingEventArgs(Player player) : base(player)
    {
    }

    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
