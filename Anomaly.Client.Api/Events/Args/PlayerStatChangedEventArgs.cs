using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>A change between observable local samples; not authoritative damage attribution.</summary>
public class PlayerStatChangedEventArgs : PlayerEventArgs
{
    public PlayerStatChangedEventArgs(Player player, PlayerStat stat, float previous, float current,
        float previousMaximum, float maximum) : base(player)
    {
        Stat = stat;
        Previous = previous;
        Current = current;
        PreviousMaximum = previousMaximum;
        Maximum = maximum;
    }

    public PlayerStat Stat { get; }
    public float Previous { get; }
    public float Current { get; }
    public float Delta => Current - Previous;
    public float PreviousMaximum { get; }
    public float Maximum { get; }
    public bool ValueChanged => Previous != Current;
    public bool MaximumChanged => PreviousMaximum != Maximum;
}
