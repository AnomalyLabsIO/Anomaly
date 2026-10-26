using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local player's <c>FpcJumpController.ProcessJump</c> is about
/// to apply a jump. Cancelling suppresses the jump motor application; the
/// server observes a continuous ground state instead of a jump.
/// </summary>
[ScriptType]
public class PlayerJumpingEventArgs : PlayerEventArgs, ICancellableEvent
{
    public PlayerJumpingEventArgs(Player player, float jumpSpeed) : base(player)
    {
        JumpSpeed = jumpSpeed;
    }

    /// <summary>The jump speed the engine was about to apply (pre-multiplier).</summary>
    [ScriptVisible]
    public float JumpSpeed { get; }

    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
