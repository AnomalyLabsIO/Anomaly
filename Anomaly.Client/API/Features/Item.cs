using System.Collections.ObjectModel;
using Il2CppInventorySystem.Items;
using UnityEngine;

namespace Anomaly.Client.API.Features;

public class Item
{
    private static readonly Dictionary<ItemBase, Item> _items = new();

    private Item(ItemBase itemBase)
    {
        Base = itemBase;
    }

    public ItemBase Base { get; }

    public ItemIdentifier Identifier => Base.ItemId;

    public int TypeId => (int)Base.ItemTypeId;

    public string TypeName => Base.ItemTypeId.ToString();

    public byte CategoryId => (byte)Base.Category;

    public string CategoryName => Base.Category.ToString();

    public byte TierFlagsValue => (byte)Base.TierFlags;

    public string TierFlagsName => Base.TierFlags.ToString();

    public ushort Serial => Base.ItemSerial;

    public Player Owner => Player.Get(Base.Owner);

    public bool IsEquipped => Base.IsEquipped;

    public bool IsLocalPlayerItem => Base.IsLocalPlayer;

    public bool IsDummyItem => Base.IsDummy;

    public bool CanEquip => Base.AllowEquip;

    public bool CanHolster => Base.AllowHolster;

    public bool CanDrop => Base.AllowDropping;

    public float Weight => Base.Weight;

    public Texture Icon => Base.Icon;

    public GameObject GameObject => Base.gameObject;

    public Transform Transform => GameObject.transform;

    public static Item Get(ItemBase itemBase)
    {
        if (itemBase == null)
            return null;

        if (_items.TryGetValue(itemBase, out Item item))
            return item;

        item = new Item(itemBase);
        _items[itemBase] = item;
        return item;
    }

    public static ReadOnlyCollection<Item> Get(IEnumerable<ItemBase> itemBases)
    {
        List<Item> items = new();
        foreach (ItemBase itemBase in itemBases)
        {
            Item item = Get(itemBase);
            if (item != null)
                items.Add(item);
        }

        return items.AsReadOnly();
    }

    public override string ToString() => $"Item({TypeName}, {Serial})";
}
