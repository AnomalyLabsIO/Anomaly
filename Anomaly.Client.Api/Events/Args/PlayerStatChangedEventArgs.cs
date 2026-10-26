using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>A change between observable local samples; not authoritative damage attribution.</summary>
[ScriptType]
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

    [ScriptVisible]
    public PlayerStat Stat { get; }
    [ScriptVisible]
    public float Previous { get; }
    [ScriptVisible]
    public float Current { get; }
    [ScriptVisible]
    public float Delta => Current - Previous;
    [ScriptVisible]
    public float PreviousMaximum { get; }
    [ScriptVisible]
    public float Maximum { get; }
    [ScriptVisible]
    public bool ValueChanged => Previous != Current;
    [ScriptVisible]
    public bool MaximumChanged => PreviousMaximum != Maximum;
}
