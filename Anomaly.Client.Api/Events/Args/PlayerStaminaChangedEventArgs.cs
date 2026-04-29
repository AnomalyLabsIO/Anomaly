using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when a tracked player's <see cref="StaminaStat"/> value crosses
/// a polling tick boundary. Delta is signed — negative for stamina drain,
/// positive for regeneration.
/// </summary>
public class PlayerStaminaChangedEventArgs : PlayerEventArgs
{
    public PlayerStaminaChangedEventArgs(Player player, float previous, float current) : base(player)
    {
        Previous = previous;
        Current = current;
    }

    public float Previous { get; }

    public float Current { get; }

    public float Delta => Current - Previous;
}
