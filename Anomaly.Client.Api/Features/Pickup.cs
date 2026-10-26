using System.Collections.Generic;
using System.Collections.ObjectModel;
using Anomaly.Client.Api.Internal.Wrappers;
using Il2Cpp;
using Il2CppInventorySystem.Items;
using Il2CppInventorySystem.Items.Pickups;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

[ScriptType]
public class Pickup
{
    private static readonly Dictionary<ItemPickupBase, Pickup> _pickups = new();
    private static readonly SerialOwnershipIndex<Pickup> _serialCache = new();
    private static readonly System.Func<ItemPickupBase, Pickup>[] _subtypeFactories =
    {
        // ThrownProjectile keeps its own cache — route through Projectile.Get so both stay in sync.
        b => CreateSubtype<Il2CppInventorySystem.Items.ThrowableProjectiles.ThrownProjectile>(b, Projectile.Get),
        b => CreateSubtype<Il2CppInventorySystem.Items.Firearms.FirearmPickup>(b, value => new FirearmPickup(value)),
        b => CreateSubtype<Il2CppInventorySystem.Items.Keycards.KeycardPickup>(b, value => new KeycardPickup(value)),
        b => CreateSubtype<Il2CppInventorySystem.Items.ThrowableProjectiles.TimedGrenadePickup>(b, value => new TimedGrenadePickup(value)),
        b => CreateSubtype<Il2CppInventorySystem.Items.Usables.Scp330.Scp330Pickup>(b, value => new Scp330Pickup(value)),
        b => CreateSubtype<Il2CppInventorySystem.Items.Usables.Scp244.Scp244DeployablePickup>(b, value => new Scp244Pickup(value)),
        b => CreateSubtype<Il2CppInventorySystem.Items.Usables.Scp1576.Scp1576Pickup>(b, value => new Scp1576Pickup(value)),
        b => CreateSubtype<Il2CppInventorySystem.Items.Firearms.Ammo.AmmoPickup>(b, value => new AmmoPickup(value)),
        b => CreateSubtype<Il2CppInventorySystem.Items.Radio.RadioPickup>(b, value => new RadioPickup(value)),
        b => CreateSubtype<Il2CppInventorySystem.Items.Armor.BodyArmorPickup>(b, value => new BodyArmorPickup(value)),
    };

    private Transform _visualRoot;

    protected Pickup(ItemPickupBase pickupBase)
    {
        Base = pickupBase;
    }

    public ItemPickupBase Base { get; }

    [ScriptVisible]
    public bool IsDestroyed => Base == null;

    [ScriptVisible]
    public ushort Serial => SafeSerial();

    public int TypeId => (int)Base.Info.ItemId;

    public string TypeName => Base.Info.ItemId.ToString();

    [ScriptVisible]
    public ItemType Type => Base.Info.ItemId;

    [ScriptVisible]
    public float Weight => Base.Info.WeightKg;

    [ScriptVisible]
    public bool CanSearch => Base.CanSearch;

    [ScriptVisible]
    public bool IsLocked => Base.Info.Locked;

    [ScriptVisible]
    public bool IsInUse => Base.Info.InUse;

    public Vector3 Position => Base.Position;

    public Quaternion Rotation => Base.Rotation;

    public Transform Transform => Base.CachedTransform;

    public GameObject GameObject => Base.gameObject;

    /// <summary>Every <see cref="Renderer"/> under this pickup (LOD0..LODn etc.). Built fresh each call.</summary>
    public IReadOnlyList<Renderer> Renderers
    {
        get
        {
            var found = GameObject.GetComponentsInChildren<Renderer>(true);
            var list = new List<Renderer>(found != null ? found.Length : 0);
            if (found == null)
                return list;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                    list.Add(found[i]);
            }
            return list;
        }
    }

    /// <summary>
    /// The visible subtree of this pickup. Heuristic: first child whose name
    /// ends with "LOD", "LODs", or "Mesh"; falls back to the root transform.
    /// Cached on first access.
    /// </summary>
    public Transform VisualRoot
    {
        get
        {
            if (_visualRoot != null)
                return _visualRoot;

            var rootT = Transform;
            if (rootT == null)
                return null;

            for (int i = 0; i < rootT.childCount; i++)
            {
                var child = rootT.GetChild(i);
                if (child == null)
                    continue;
                var n = child.name;
                if (n != null && (n.EndsWith("LOD") || n.EndsWith("LODs") || n.EndsWith("Mesh")))
                {
                    _visualRoot = child;
                    return _visualRoot;
                }
            }

            _visualRoot = rootT;
            return _visualRoot;
        }
    }

    /// <summary>
    /// Clones this pickup as a pure-visual GameObject (physics / network /
    /// pickup behaviour stripped) and optionally parents it under
    /// <paramref name="parent"/>. See <see cref="ItemModels.InstantiateVisual(GameObject, Transform)"/>.
    /// </summary>
    public GameObject InstantiateVisual(Transform parent = null) =>
        ItemModels.InstantiateVisual(GameObject, parent);

    /// <summary>
    /// All currently tracked pickups. Cheap — iterates the wrapper cache,
    /// not the Unity scene.
    /// </summary>
    public static ReadOnlyCollection<Pickup> List
    {
        get
        {
            List<Pickup> snapshot = new(_pickups.Count);
            foreach (Pickup p in _pickups.Values)
            {
                if (p != null && p.Base != null)
                    snapshot.Add(p);
            }
            return snapshot.AsReadOnly();
        }
    }

    /// <summary>
    /// Gets or creates the wrapper for the given <see cref="ItemPickupBase"/>.
    /// Returns <c>null</c> if <paramref name="pickupBase"/> is null.
    /// </summary>
    public static Pickup Get(ItemPickupBase pickupBase)
    {
        if (pickupBase == null)
            return null;

        if (_pickups.TryGetValue(pickupBase, out Pickup pickup))
        {
            RefreshSerialIndex(pickup);
            return pickup;
        }

        pickup = CreateForType(pickupBase);
        if (pickup == null)
            return null;

        _pickups[pickupBase] = pickup;
        RefreshSerialIndex(pickup);
        return pickup;
    }

    /// <summary>
    /// Convenience downcast. Returns <c>null</c> if this wrapper isn't a <typeparamref name="T"/>.
    /// </summary>
    public T As<T>() where T : Pickup => this as T;

    /// <summary>True if this wrapper is a <typeparamref name="T"/>.</summary>
    public bool Is<T>() where T : Pickup => this is T;

    private static Pickup CreateForType(ItemPickupBase pickupBase)
    {
        // TryCast uses the native type even when the caller holds a managed base proxy.
        foreach (var factory in _subtypeFactories)
        {
            Pickup pickup = factory(pickupBase);
            if (pickup != null)
                return pickup;
        }
        return new Pickup(pickupBase);
    }

    private static Pickup CreateSubtype<T>(ItemPickupBase pickupBase, System.Func<T, Pickup> factory) where T : ItemPickupBase
    {
        T typed = pickupBase.TryCast<T>();
        return typed != null ? factory(typed) : null;
    }

    /// <summary>
    /// Look up a pickup by its serial. Returns <c>null</c> if no pickup with
    /// that serial is currently tracked. Pickups spawned by lockers may not
    /// have a serial until they are unlocked, so they will not appear in the
    /// serial cache until <see cref="Get(ItemPickupBase)"/> is called on them
    /// after the serial is assigned.
    /// </summary>
    public static Pickup Get(ushort serial)
    {
        TryGet(serial, out Pickup pickup);
        return pickup;
    }

    /// <inheritdoc cref="Get(ushort)"/>
    public static bool TryGet(ushort serial, out Pickup pickup)
    {
        if (serial == 0)
        {
            pickup = null;
            return false;
        }
        if (!_serialCache.TryGet(serial, out pickup))
            return false;
        if (!pickup.IsDestroyed)
            return true;
        _serialCache.RemoveIfOwned(serial, pickup);
        pickup = null;
        return false;
    }

    public static bool TryGet(ItemPickupBase pickupBase, out Pickup pickup)
    {
        pickup = Get(pickupBase);
        return pickup != null;
    }

    /// <summary>
    /// All currently tracked pickups whose <see cref="Type"/> matches <paramref name="type"/>.
    /// </summary>
    public static ReadOnlyCollection<Pickup> GetAll(ItemType type)
    {
        List<Pickup> matches = new();
        foreach (Pickup p in _pickups.Values)
        {
            if (p == null || p.Base == null)
                continue;
            if (p.Type == type)
                matches.Add(p);
        }
        return matches.AsReadOnly();
    }

    /// <summary>
    /// Removes <paramref name="pickupBase"/> from the wrapper cache. Called by
    /// the loader on <see cref="ItemPickupBase.OnDestroy"/>; mods should not
    /// need to call this.
    /// </summary>
    internal static bool Unregister(ItemPickupBase pickupBase)
    {
        if (pickupBase == null)
            return false;

        if (!_pickups.TryGetValue(pickupBase, out Pickup pickup))
            return false;

        pickup.OnRemove();
        return true;
    }

    /// <summary>
    /// Drops every cached pickup. Called by the loader on client disconnect to
    /// prevent stale wrappers from leaking across server sessions.
    /// </summary>
    internal static void ClearAll()
    {
        _pickups.Clear();
        _serialCache.Clear();
    }

    /// <summary>
    /// Hook for subclasses (e.g. <see cref="Projectile"/>) to evict from
    /// their own caches in addition to the base pickup cache.
    /// </summary>
    internal virtual void OnRemove()
    {
        if (Base != null)
            _pickups.Remove(Base);

        ushort serial = SafeSerial();
        _serialCache.RemoveIfOwned(serial, this);
    }

    /// <summary>
    /// Updates <see cref="_serialCache"/> if the wrapper now reports a non-zero
    /// serial that isn't yet indexed. Locker pickups in particular start with
    /// serial 0 and acquire one on unlock.
    /// </summary>
    private static void RefreshSerialIndex(Pickup pickup)
    {
        ushort serial = pickup.SafeSerial();
        if (serial == 0)
            return;

        Pickup existing = _serialCache.Assign(serial, pickup);
        if (existing != null)
            _pickups.Remove(existing.Base);
    }

    /// <summary>
    /// Reads the serial without throwing if the underlying IL2CPP object has
    /// already been collected. Returns 0 if unreadable.
    /// </summary>
    private ushort SafeSerial()
    {
        try
        {
            if (Base == null)
                return 0;
            return Base.Info.Serial;
        }
        catch
        {
            return 0;
        }
    }

    public override string ToString() => $"Pickup({TypeName}, {Serial})";
}
