using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class EffectEnabledEventArgs : EventArgs
{
    public EffectEnabledEventArgs(PlayerEffect effect)
    {
        Effect = effect;
    }

    public PlayerEffect Effect { get; }
}
