using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class PlayerAddedEventArgs : PlayerEventArgs
{
    public PlayerAddedEventArgs(Player player) : base(player)
    {
    }
}
