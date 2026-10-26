using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class EffectDisabledEventArgs : EventArgs
{
    public EffectDisabledEventArgs(PlayerEffect effect)
    {
        Effect = effect;
    }

    [ScriptVisible]
    public PlayerEffect Effect { get; }
}
