using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class PlayerEventArgs : EventArgs, IPlayerEvent
{
    public PlayerEventArgs(Player player)
    {
        Player = player;
    }

    public Player Player { get; }
}
