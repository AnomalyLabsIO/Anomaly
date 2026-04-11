using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class EffectEnabledEventArgs : EventArgs
{
    public EffectEnabledEventArgs(PlayerEffect effect)
    {
        Effect = effect;
    }

    public PlayerEffect Effect { get; }
}
