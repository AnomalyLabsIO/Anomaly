using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class EffectDisabledEventArgs : EventArgs
{
    public EffectDisabledEventArgs(PlayerEffect effect)
    {
        Effect = effect;
    }

    public PlayerEffect Effect { get; }
}
