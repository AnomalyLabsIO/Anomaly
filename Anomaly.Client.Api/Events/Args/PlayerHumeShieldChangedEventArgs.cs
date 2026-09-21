using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when a tracked player's <c>HumeShieldStat</c> value crosses
/// a polling tick boundary. Delta is signed — negative for shield damage,
/// positive for regeneration. The convenience <see cref="Broke"/> property
/// is true on the tick where the shield drops to zero from a non-zero value
/// (mirrors LabAPI's server-side <c>HumeShieldBroken</c> event).
/// </summary>
public class PlayerHumeShieldChangedEventArgs : PlayerEventArgs
{
    public PlayerHumeShieldChangedEventArgs(Player player, float previous, float current) : base(player)
    {
        Previous = previous;
        Current = current;
    }

    public float Previous { get; }

    public float Current { get; }

    public float Delta => Current - Previous;

    /// <summary>True if the shield broke (dropped to zero) on this tick.</summary>
    public bool Broke => Previous > 0f && Current <= 0f;
}
