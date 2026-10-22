using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>A newly received SCP-079 total-experience observation.</summary>
public sealed class Scp079ExperienceChangedEventArgs : PlayerEventArgs
{
    public Scp079ExperienceChangedEventArgs(Player player, int? previous, int current) : base(player)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>The previous received value, or null before the first update for this role life.</summary>
    public int? Previous { get; }

    public int Current { get; }

    public bool IsInitialObservation => !Previous.HasValue;
}
