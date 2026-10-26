using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>A cooldown notification received for an item in the local inventory.</summary>
[ScriptType]
public class UsableItemCooldownEventArgs : EventArgs, IItemEvent, IPlayerEvent
{
    public UsableItemCooldownEventArgs(UsableItem item, float remainingTime)
    {
        Item = item;
        Player = item?.CurrentOwner;
        ItemSerial = item?.Serial ?? 0;
        RemainingTime = remainingTime;
    }

    [ScriptVisible]
    public ushort ItemSerial { get; }
    [ScriptVisible]
    public Item Item { get; }
    public UsableItem UsableItem => Item as UsableItem;
    [ScriptVisible]
    public Player Player { get; }

    /// <summary>Seconds supplied in this notification, not a continuously updated countdown.</summary>
    [ScriptVisible]
    public float RemainingTime { get; }
}
