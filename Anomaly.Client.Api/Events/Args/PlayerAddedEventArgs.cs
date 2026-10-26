using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class PlayerAddedEventArgs : PlayerEventArgs
{
    public PlayerAddedEventArgs(Player player) : base(player)
    {
    }
}
