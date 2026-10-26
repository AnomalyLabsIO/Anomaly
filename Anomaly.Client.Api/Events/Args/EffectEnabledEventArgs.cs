using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class EffectEnabledEventArgs : EventArgs
{
    public EffectEnabledEventArgs(PlayerEffect effect)
    {
        Effect = effect;
    }

    [ScriptVisible]
    public PlayerEffect Effect { get; }
}
