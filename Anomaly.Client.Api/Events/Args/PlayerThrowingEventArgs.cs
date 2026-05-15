using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local player initiates a throw (primary or secondary fire on
/// a held throwable) before the <c>BeginThrow</c> network message is sent.
/// Cancelling stops the throw stopwatch from starting and skips the request.
/// </summary>
public class PlayerThrowingEventArgs : PlayerEventArgs, ICancellableEvent
{
    
    public PlayerThrowingEventArgs(Player player, ThrowableItem item) : base(player)
    {
        Item = item;
    }
    public ThrowableItem Item { get; private set; }
    public bool IsAllowed { get; set; } = true;
}
