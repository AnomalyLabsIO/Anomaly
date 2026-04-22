using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class EffectIntensityChangedEventArgs : EventArgs
{
    public EffectIntensityChangedEventArgs(PlayerEffect effect, byte previousIntensity, byte currentIntensity)
    {
        Effect = effect;
        PreviousIntensity = previousIntensity;
        CurrentIntensity = currentIntensity;
    }

    public PlayerEffect Effect { get; }

    public byte PreviousIntensity { get; }

    public byte CurrentIntensity { get; }
}
