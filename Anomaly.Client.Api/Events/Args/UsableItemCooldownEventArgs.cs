using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>A cooldown notification received for an item in the local inventory.</summary>
public class UsableItemCooldownEventArgs : EventArgs, IItemEvent, IPlayerEvent
{
    public UsableItemCooldownEventArgs(UsableItem item, float remainingTime)
    {
        Item = item;
        Player = item?.CurrentOwner;
        ItemSerial = item?.Serial ?? 0;
        RemainingTime = remainingTime;
    }

    public ushort ItemSerial { get; }
    public Item Item { get; }
    public UsableItem UsableItem => Item as UsableItem;
    public Player Player { get; }

    /// <summary>Seconds supplied in this notification, not a continuously updated countdown.</summary>
    public float RemainingTime { get; }
}
