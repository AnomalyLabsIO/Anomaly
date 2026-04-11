using System.Collections.ObjectModel;
using Il2CppInventorySystem.Items;
using Il2CppInventorySystem.Items.Pickups;
using UnityEngine;

namespace Anomaly.Client.API.Features;

public class Pickup
{
    private static readonly Dictionary<ItemPickupBase, Pickup> _pickups = new();

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
