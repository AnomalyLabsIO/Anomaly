using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local player initiates a drop via <c>Inventory.ClientDropItem</c>,
/// before the <c>CmdDropItem</c> RPC is sent. Cancelling keeps the item in the
/// local inventory; the server never sees the drop request.
/// </summary>
public class PlayerDroppingItemEventArgs : PlayerEventArgs, ICancellableEvent, IItemEvent
{
    public PlayerDroppingItemEventArgs(Player player, ushort itemSerial, bool tryThrow) : base(player)
    {
        ItemSerial = itemSerial;
        Item = Features.Item.Get(itemSerial);
        TryThrow = tryThrow;
    }

    /// <summary>Serial of the item the player is attempting to drop.</summary>
    public ushort ItemSerial { get; }

    /// <summary>The item when it can be resolved from the client cache.</summary>
    public Item Item { get; }

    /// <summary>True if the player is performing a drop-and-throw (G + movement), false for a plain drop.</summary>
    public bool TryThrow { get; }

    public bool IsAllowed { get; set; } = true;
}
