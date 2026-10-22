using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>A newly received SCP-079 auxiliary power observation.</summary>
public sealed class Scp079AuxiliaryPowerChangedEventArgs : PlayerEventArgs
{
    public Scp079AuxiliaryPowerChangedEventArgs(Player player, float? previous, float current) : base(player)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>The previous received value, or null before the first update in this viewing context.</summary>
    public float? Previous { get; }

    public float Current { get; }

    public bool IsInitialObservation => !Previous.HasValue;
}
