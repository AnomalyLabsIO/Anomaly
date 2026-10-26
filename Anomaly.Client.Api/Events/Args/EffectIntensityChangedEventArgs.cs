using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class EffectIntensityChangedEventArgs : EventArgs
{
    public EffectIntensityChangedEventArgs(PlayerEffect effect, byte previousIntensity, byte currentIntensity)
    {
        Effect = effect;
        PreviousIntensity = previousIntensity;
        CurrentIntensity = currentIntensity;
    }

    [ScriptVisible]
    public PlayerEffect Effect { get; }

    [ScriptVisible]
    public byte PreviousIntensity { get; }

    [ScriptVisible]
    public byte CurrentIntensity { get; }
}
