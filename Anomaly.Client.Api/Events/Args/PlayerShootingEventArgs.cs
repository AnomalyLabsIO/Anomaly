using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires on the rising edge of the local player's trigger press, immediately
/// before <c>SimpleTriggerModule</c> would send the trigger-held command to the
/// server. Cancelling suppresses the local fire and the wire message.
/// </summary>
public class PlayerShootingEventArgs : PlayerEventArgs, ICancellableEvent
{
    public PlayerShootingEventArgs(Player player) : base(player)
    {
    }

    public bool IsAllowed { get; set; } = true;
}
