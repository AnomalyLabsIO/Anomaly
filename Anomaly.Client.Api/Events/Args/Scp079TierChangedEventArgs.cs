using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>A newly received SCP-079 access-tier observation.</summary>
public sealed class Scp079TierChangedEventArgs : PlayerEventArgs
{
    public Scp079TierChangedEventArgs(Player player, int? previous, int current) : base(player)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>The previous one-based tier, or null before the first update for this role life.</summary>
    public int? Previous { get; }

    /// <summary>The newly observed one-based tier.</summary>
    public int Current { get; }

    public bool IsInitialObservation => !Previous.HasValue;
}
