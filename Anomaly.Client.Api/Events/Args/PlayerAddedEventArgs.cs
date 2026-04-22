using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class PlayerAddedEventArgs : PlayerEventArgs
{
    public PlayerAddedEventArgs(Player player) : base(player)
    {
    }
}
