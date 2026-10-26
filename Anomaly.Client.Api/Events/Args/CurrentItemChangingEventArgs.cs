using Anomaly.Client.Api.Features;
using Il2CppInventorySystem.Items;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local player initiates a held-item switch via
/// <c>Inventory.ClientSelectItem</c> (mouse wheel / number keys), before the
/// <c>CmdSelectItem</c> RPC is sent. Cancelling keeps the current held item;
/// the server never sees the switch request.
///
/// Note: server-forced switches (e.g., death holster) arrive via the
/// <c>NetworkCurItem</c> SyncVar hook and fire <c>CurrentItemChanged</c>
/// (post-action) instead — they do not pass through this path.
/// </summary>
// TODO: Replace ItemIdentifier type with Item once Serial→Item mapping is available,
// matching the convention used by CurrentItemChangedEventArgs.
[ScriptType]
public class CurrentItemChangingEventArgs : PlayerEventArgs, ICancellableEvent
{
    public CurrentItemChangingEventArgs(Player player, ItemIdentifier previousItem, ushort targetSerial) : base(player)
    {
        PreviousItem = previousItem;
        TargetSerial = targetSerial;
    }

    /// <summary>The item currently held (may be <see cref="ItemIdentifier.None"/>).</summary>
    public ItemIdentifier PreviousItem { get; }

    /// <summary>The serial held before this local switch request.</summary>
    [ScriptVisible]
    public ushort PreviousSerial => PreviousItem.SerialNumber;

    /// <summary>Serial of the item being switched to (0 for holster/no item).</summary>
    [ScriptVisible]
    public ushort TargetSerial { get; }

    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
