using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class PlayerRemovedEventArgs : PlayerEventArgs
{
    public PlayerRemovedEventArgs(Player player) : base(player)
    {
    }
}
