using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class InventoryChangedEventArgs : PlayerEventArgs
{
    public InventoryChangedEventArgs(Player player) : base(player)
    {
    }
}
