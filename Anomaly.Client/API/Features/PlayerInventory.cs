using System.Collections.ObjectModel;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;

namespace Anomaly.Client.API.Features;

public class PlayerInventory
{
    private static readonly Dictionary<Inventory, PlayerInventory> _inventories = new();

    private PlayerInventory(Inventory inventory)
    {
        Base = inventory;
    }

    public Inventory Base { get; }

    public ItemIdentifier CurrentItemIdentifier => Base.CurItem;

    public Item CurrentItem => Item.Get(Base.CurInstance);

    public int MaxSlots => Inventory.MaxSlots;

    public int Count => Base.UserInventory.Items.Count;

    public bool IsEmpty => Count == 0;

    public bool IsFull => Count >= MaxSlots;

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
