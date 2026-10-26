using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;
[ScriptType]
public class PlayerRemovedEventArgs : PlayerEventArgs
{
    public PlayerRemovedEventArgs(Player player) : base(player)
    {
    }
}
