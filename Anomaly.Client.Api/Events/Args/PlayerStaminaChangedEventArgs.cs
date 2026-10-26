using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when a tracked player's <c>StaminaStat</c> value crosses
/// a polling tick boundary. Delta is signed — negative for stamina drain,
/// positive for regeneration.
/// </summary>
[ScriptType]
public class PlayerStaminaChangedEventArgs : PlayerEventArgs
{
    public PlayerStaminaChangedEventArgs(Player player, float previous, float current) : base(player)
    {
        Previous = previous;
        Current = current;
    }

    [ScriptVisible]
    public float Previous { get; }

    [ScriptVisible]
    public float Current { get; }

    [ScriptVisible]
    public float Delta => Current - Previous;
}
