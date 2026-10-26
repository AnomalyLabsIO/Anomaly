using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>A newly received SCP-079 auxiliary power observation.</summary>
[ScriptType]
public sealed class Scp079AuxiliaryPowerChangedEventArgs : PlayerEventArgs
{
    public Scp079AuxiliaryPowerChangedEventArgs(Player player, float? previous, float current) : base(player)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>The previous received value, or null before the first update in this viewing context.</summary>
    [ScriptVisible]
    public float? Previous { get; }

    [ScriptVisible]
    public float Current { get; }

    [ScriptVisible]
    public bool IsInitialObservation => !Previous.HasValue;
}
