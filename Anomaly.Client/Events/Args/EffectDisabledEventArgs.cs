using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class EffectDisabledEventArgs : EventArgs
{
    public EffectDisabledEventArgs(PlayerEffect effect)
    {
        Effect = effect;
    }

    public PlayerEffect Effect { get; }
}
