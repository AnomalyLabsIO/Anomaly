using System.Collections.ObjectModel;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;

namespace Anomaly.Client.Api.Features;

public class PlayerInventory
{
    private static readonly Dictionary<Inventory, PlayerInventory> _inventories = new();

    private PlayerInventory(Inventory inventory)
    {
        Base = inventory;
    }

    public Inventory Base { get; }

    /// <summary>
    /// The currently equipped item identifier. Replicated to all clients via
    /// <c>[SyncVar] Inventory.CurItem</c>, so this value is meaningful for both
    /// the local player and remote players.
    /// </summary>
    public ItemIdentifier CurrentItemIdentifier => Base.CurItem;

    /// <summary>
    /// Wrapper around the currently equipped item. For remote players the
    /// underlying <see cref="ItemBase"/> is materialized just enough to drive
    /// third-person rendering, so this is non-null when they have something
    /// equipped. Returns <c>null</c> when nothing is equipped.
    /// </summary>
    public Item CurrentItem => Item.Get(Base.CurInstance);

    public int MaxSlots => Inventory.MaxSlots;

    /// <summary>
    /// Item count in the local inventory data structure.
    ///
    /// <para><b>Local-only.</b> The full inventory is replicated to clients via
    /// <c>Inventory.TargetRefreshItems()</c>, which is a Mirror <c>TargetRpc</c>
    /// — i.e. it only reaches the owning client. For remote players this is
    /// always 0, regardless of what they're actually carrying. Use
    /// <see cref="CurrentItem"/> if you need to know what a remote player has
    /// in hand.</para>
    /// </summary>
    public int Count => Base.UserInventory.Items.Count;

    public bool IsEmpty => Count == 0;

    public bool IsFull => Count >= MaxSlots;

    /// <summary>
    /// All items in the local inventory data structure.
    ///
    /// <para><b>Local-only.</b> Returns an empty collection for remote players
    /// — the wire protocol does not replicate non-equipped inventory contents
    /// to other clients. For remote-player visibility, only
    /// <see cref="CurrentItem"/> is reliable. See <see cref="Item.Get(ushort)"/>
    /// for the same caveat applied to serial lookups.</para>
    /// </summary>
    public ReadOnlyCollection<Item> Items
    {
        get
        {
            List<Item> items = new();
            foreach (var pair in Base.UserInventory.Items)
            {
                Item item = Item.Get(pair.Value);
                if (item != null)
                    items.Add(item);
            }

            return items.AsReadOnly();
        }
    }

    /// <summary>
    /// Reserve ammunition counts by ammo type.
    ///
    /// <para><b>Local-only.</b> Same scope as <see cref="Items"/>: ammo is part
    /// of the <c>TargetRefreshAmmo</c> TargetRpc and is never sent to remote
    /// clients. Returns an empty dictionary for non-local players.</para>
    /// </summary>
    public ReadOnlyDictionary<string, ushort> Ammo
    {
        get
        {
            Dictionary<string, ushort> ammo = new();
            foreach (var pair in Base.UserInventory.ReserveAmmo)
            {
                ammo[pair.Key.ToString()] = pair.Value;
            }

            return new ReadOnlyDictionary<string, ushort>(ammo);
        }
    }

    public static PlayerInventory Get(Inventory inventory)
    {
        if (inventory == null)
            return null;

        if (_inventories.TryGetValue(inventory, out PlayerInventory wrapper))
            return wrapper;

        wrapper = new PlayerInventory(inventory);
        _inventories[inventory] = wrapper;
        return wrapper;
    }

    public override string ToString() => $"PlayerInventory({Count}/{MaxSlots})";
}
