using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class InventoryChangedEventArgs : PlayerEventArgs
{
    public InventoryChangedEventArgs(Player player) : base(player)
    {
    }
}
