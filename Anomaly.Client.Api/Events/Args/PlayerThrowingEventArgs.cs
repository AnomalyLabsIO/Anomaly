using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local player initiates a throw (primary or secondary fire on
/// a held throwable) before the <c>BeginThrow</c> network message is sent.
/// Cancelling stops the throw stopwatch from starting and skips the request.
/// </summary>
[ScriptType]
public class PlayerThrowingEventArgs : PlayerEventArgs, ICancellableEvent
{
    
    public PlayerThrowingEventArgs(Player player, ThrowableItem item) : base(player)
    {
        Item = item;
    }
    [ScriptVisible]
    public ThrowableItem Item { get; private set; }
    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
