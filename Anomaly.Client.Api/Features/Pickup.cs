using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppInventorySystem.Items;
using Il2CppInventorySystem.Items.Pickups;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

public class Pickup
{
    private static readonly Dictionary<ItemPickupBase, Pickup> _pickups = new();

    private Transform _visualRoot;

    protected Pickup(ItemPickupBase pickupBase)
    {
        Base = pickupBase;
    }

    public ItemPickupBase Base { get; }

    public ushort Serial => Base.Info.Serial;

    public int TypeId => (int)Base.Info.ItemId;

    public string TypeName => Base.Info.ItemId.ToString();

    public float Weight => Base.Info.WeightKg;

    public bool CanSearch => Base.CanSearch;

    public bool IsLocked => Base.Info.Locked;

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

    public static ReadOnlyCollection<Pickup> List
    {
        get
        {
            List<Pickup> pickups = new();
            foreach (ItemPickupBase pickupBase in UnityEngine.Object.FindObjectsOfType<ItemPickupBase>())
            {
                Pickup pickup = Get(pickupBase);
                if (pickup != null)
                    pickups.Add(pickup);
            }

            return pickups.AsReadOnly();
        }
    }

    public static Pickup Get(ItemPickupBase pickupBase)
    {
        if (pickupBase == null)
            return null;

        if (_pickups.TryGetValue(pickupBase, out Pickup pickup))
            return pickup;

        pickup = pickupBase is Il2CppInventorySystem.Items.ThrowableProjectiles.ThrownProjectile thrownProjectile
            ? Projectile.Get(thrownProjectile)
            : new Pickup(pickupBase);

        _pickups[pickupBase] = pickup;
        return pickup;
    }

    public override string ToString() => $"Pickup({TypeName}, {Serial})";
}
