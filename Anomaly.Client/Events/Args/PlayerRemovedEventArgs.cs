using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class PlayerRemovedEventArgs : PlayerEventArgs
{
    public PlayerRemovedEventArgs(Player player) : base(player)
    {
    }
}
