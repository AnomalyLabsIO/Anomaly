using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;
[ScriptType]
public class PlayerEventArgs : EventArgs, IPlayerEvent
{
    public PlayerEventArgs(Player player)
    {
        Player = player;
    }

    [ScriptVisible]
    public Player Player { get; }
}
