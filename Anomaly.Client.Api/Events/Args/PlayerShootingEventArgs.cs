using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires on the rising edge of the local player's trigger press, immediately
/// before <c>SimpleTriggerModule</c> would send the trigger-held command to the
/// server. Cancelling suppresses the local fire and the wire message.
/// </summary>
[ScriptType]
public class PlayerShootingEventArgs : PlayerEventArgs, ICancellableEvent
{
    public PlayerShootingEventArgs(Player player) : base(player)
    {
    }

    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
