using System.Collections.Generic;
using System.Collections.ObjectModel;
using Anomaly.Client.Api.Internal.Wrappers;
using Il2Cpp;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

public class Item
{
    private static readonly Dictionary<ItemBase, Item> _items = new();
    private static readonly SerialOwnershipIndex<Item> _serialCache = new();
    private static readonly Dictionary<System.Type, System.Func<ItemBase, Item>> _subtypeFactories = new()
    {
        { typeof(Il2CppInventorySystem.Items.Firearms.Firearm), b => new FirearmItem((Il2CppInventorySystem.Items.Firearms.Firearm)b) },
        { typeof(Il2CppInventorySystem.Items.Keycards.KeycardItem), b => new KeycardItem((Il2CppInventorySystem.Items.Keycards.KeycardItem)b) },
        { typeof(Il2CppInventorySystem.Items.ThrowableProjectiles.ThrowableItem), b => new ThrowableItem((Il2CppInventorySystem.Items.ThrowableProjectiles.ThrowableItem)b) },
        { typeof(Il2CppInventorySystem.Items.Usables.Scp330.Scp330Bag), b => new Scp330Item((Il2CppInventorySystem.Items.Usables.Scp330.Scp330Bag)b) },
    };

    protected Item(ItemBase itemBase)
    {
        Base = itemBase;
    }

    public ItemBase Base { get; }

    public ItemIdentifier Identifier => Base.ItemId;

    public int TypeId => (int)Base.ItemTypeId;

    public string TypeName => Base.ItemTypeId.ToString();

    public ItemType Type => Base.ItemTypeId;

    public byte CategoryId => (byte)Base.Category;

    public string CategoryName => Base.Category.ToString();

    public ItemCategory Category => Base.Category;

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

    /// <summary>
    /// Gets or creates the wrapper for the given <see cref="ItemBase"/>.
    /// Returns <c>null</c> if <paramref name="itemBase"/> is null. Dispatches
    /// to a subtype wrapper (e.g. <see cref="FirearmItem"/>,
    /// <see cref="KeycardItem"/>) when one is registered for the runtime type.
    /// </summary>
    public static Item Get(ItemBase itemBase)
    {
        if (itemBase == null)
            return null;

        if (_items.TryGetValue(itemBase, out Item item))
        {
            RefreshSerialIndex(item);
            return item;
        }

        item = CreateForType(itemBase);
        _items[itemBase] = item;
        RefreshSerialIndex(item);
        return item;
    }

    /// <summary>
    /// Convenience downcast. Returns <c>null</c> if this wrapper isn't a
    /// <typeparamref name="T"/>. Equivalent to <c>this as T</c> but discoverable
    /// from the base class's IntelliSense.
    /// </summary>
    public T As<T>() where T : Item => this as T;

    /// <summary>True if this wrapper is a <typeparamref name="T"/>.</summary>
    public bool Is<T>() where T : Item => this is T;

    private static Item CreateForType(ItemBase itemBase)
    {
        // Walk the type hierarchy looking for a registered subtype factory.
        // Allows derived game types (e.g. RevolverFirearm extends Firearm) to
        // fall back to the FirearmItem wrapper if no specific revolver wrapper
        // is registered.
        System.Type t = itemBase.GetType();
        while (t != null && t != typeof(ItemBase))
        {
            if (_subtypeFactories.TryGetValue(t, out var factory))
                return factory(itemBase);
            t = t.BaseType;
        }
        return new Item(itemBase);
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

    /// <summary>
    /// Look up an item by its serial.
    ///
    /// <para><b>Local-only scope.</b> Unlike LabAPI's server-side
    /// <c>Item.Get(serial)</c>, the client only knows about <see cref="ItemBase"/>
    /// instances for (a) the local player's full inventory, and (b) items
    /// currently equipped on remote players (just enough to drive third-person
    /// rendering). Items in remote players' non-equipped inventory are not
    /// replicated and cannot be resolved by serial here. This is a wire-protocol
    /// limit, not an API gap.</para>
    ///
    /// <para>For pickups in the world, use <see cref="Pickup.Get(ushort)"/>.</para>
    /// </summary>
    public static Item Get(ushort serial)
    {
        if (serial == 0)
            return null;
        _serialCache.TryGet(serial, out Item item);
        return item;
    }

    /// <inheritdoc cref="Get(ushort)"/>
    public static bool TryGet(ushort serial, out Item item)
    {
        if (serial == 0)
        {
            item = null;
            return false;
        }
        return _serialCache.TryGet(serial, out item);
    }

    /// <summary>
    /// All currently tracked items whose <see cref="Type"/> matches
    /// <paramref name="type"/>. Same scope caveat as <see cref="Get(ushort)"/>:
    /// only resolvable items on the client are returned.
    /// </summary>
    public static ReadOnlyCollection<Item> GetAll(ItemType type)
    {
        List<Item> matches = new();
        foreach (Item item in _items.Values)
        {
            if (item == null || item.Base == null)
                continue;
            if (item.Type == type)
                matches.Add(item);
        }
        return matches.AsReadOnly();
    }

    /// <summary>
    /// All currently tracked items whose <see cref="Category"/> matches
    /// <paramref name="category"/>. Same scope caveat as <see cref="Get(ushort)"/>.
    /// </summary>
    public static ReadOnlyCollection<Item> GetAll(ItemCategory category)
    {
        List<Item> matches = new();
        foreach (Item item in _items.Values)
        {
            if (item == null || item.Base == null)
                continue;
            if (item.Category == category)
                matches.Add(item);
        }
        return matches.AsReadOnly();
    }

    /// <summary>
    /// Removes <paramref name="itemBase"/> from the wrapper cache. Called by
    /// the loader when an item is destroyed or the local player disconnects;
    /// mods should not need to call this.
    /// </summary>
    internal static bool Unregister(ItemBase itemBase)
    {
        if (itemBase == null)
            return false;

        if (!_items.TryGetValue(itemBase, out Item item))
            return false;

        _items.Remove(itemBase);

        ushort serial = item.SafeSerial();
        _serialCache.RemoveIfOwned(serial, item);

        return true;
    }

    internal static bool Unregister(ushort serial)
    {
        if (!_serialCache.TryGet(serial, out Item item))
            return false;
        return Unregister(item.Base);
    }

    /// <summary>
    /// Removes cached wrappers for items that are no longer present in one
    /// player's replicated inventory snapshot.
    /// </summary>
    internal static void ReconcileInventory(Inventory inventory)
    {
        if (inventory?._hub == null)
            return;

        var live = new HashSet<ItemBase>();
        foreach (var pair in inventory.UserInventory.Items)
            if (pair.Value != null) live.Add(pair.Value);

        var stale = new List<ItemBase>();
        foreach (var pair in _items)
        {
            ItemBase itemBase = pair.Key;
            if (itemBase == null)
            {
                stale.Add(itemBase);
                continue;
            }

            try
            {
                if (itemBase.Owner == inventory._hub && !live.Contains(itemBase))
                    stale.Add(itemBase);
            }
            catch
            {
                stale.Add(itemBase);
            }
        }

        foreach (var itemBase in stale)
        {
            if (itemBase == null)
                RemoveNullOrDestroyedEntries();
            else
                Unregister(itemBase);
        }
    }

    /// <summary>
    /// Drops every cached item. Called by the loader on client disconnect to
    /// prevent stale wrappers from leaking across server sessions.
    /// </summary>
    internal static void ClearAll()
    {
        _items.Clear();
        _serialCache.Clear();
    }

    private static void RemoveNullOrDestroyedEntries()
    {
        var stale = new List<ItemBase>();
        foreach (var itemBase in _items.Keys)
            if (itemBase == null) stale.Add(itemBase);
        foreach (var itemBase in stale)
            _items.Remove(itemBase);

        _serialCache.RemoveWhere(item => item?.Base == null);
    }

    private static void RefreshSerialIndex(Item item)
    {
        ushort serial = item.SafeSerial();
        if (serial == 0)
            return;

        Item existing = _serialCache.Assign(serial, item);
        if (existing != null)
            _items.Remove(existing.Base);
    }

    private ushort SafeSerial()
    {
        try
        {
            if (Base == null)
                return 0;
            return Base.ItemSerial;
        }
        catch
        {
            return 0;
        }
    }

    public override string ToString() => $"Item({TypeName}, {Serial})";
}
