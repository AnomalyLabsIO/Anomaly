using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class PlayerEventArgs : EventArgs
{
    public PlayerEventArgs(Player player)
    {
        Player = player;
    }

    public Player Player { get; }
}
