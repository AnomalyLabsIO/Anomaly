using Anomaly.Client.Api.Features;
using Il2CppInventorySystem.Items;

namespace Anomaly.Client.Api.Events.Args;

public class CurrentItemChangedEventArgs : PlayerEventArgs
{
    // todo: Replace ItemIdentifier type with Item when we have a way to map Serial to Items
    public CurrentItemChangedEventArgs(Player player, ItemIdentifier previousItem, ItemIdentifier currentItem) : base(player)
    {
        PreviousItem = previousItem;
        CurrentItem = currentItem;
    }

    public ItemIdentifier PreviousItem { get; }

    public ItemIdentifier CurrentItem { get; }
}
