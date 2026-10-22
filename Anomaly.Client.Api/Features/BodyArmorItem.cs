using System.Collections.ObjectModel;
using Il2Cpp;
using Il2CppInventorySystem.Items;
using ArmorBase = Il2CppInventorySystem.Items.Armor.BodyArmor;

namespace Anomaly.Client.Api.Features;

/// <summary>Body armor configuration and owning-client wear state.</summary>
public class BodyArmorItem : Item
{
    internal BodyArmorItem(ArmorBase armorBase) : base(armorBase)
    {
        ArmorBase = armorBase;
    }

    public ArmorBase ArmorBase { get; }
    public int HelmetEfficacy => ArmorBase.HelmetEfficacy;
    public int VestEfficacy => ArmorBase.VestEfficacy;
    public bool? IsWorn => !IsDestroyed && IsLocalPlayerItem ? ArmorBase.IsWorn : null;

    /// <summary>Owner-dependent movement modifier, available only for the local inventory.</summary>
    public float? MovementSpeedMultiplier => !IsDestroyed && IsLocalPlayerItem ? ArmorBase.MovementSpeedMultiplier : null;
    public float? StaminaUsageMultiplier => !IsDestroyed && IsLocalPlayerItem ? ArmorBase.StaminaUsageMultiplier : null;

    public ReadOnlyDictionary<ItemType, ushort> AmmoLimits
    {
        get
        {
            var limits = new Dictionary<ItemType, ushort>();
            if (ArmorBase.AmmoLimits != null)
                foreach (var limit in ArmorBase.AmmoLimits)
                    limits[limit.AmmoType] = limit.Limit;
            return new ReadOnlyDictionary<ItemType, ushort>(limits);
        }
    }

    public ReadOnlyDictionary<ItemCategory, byte> CategoryLimits
    {
        get
        {
            var limits = new Dictionary<ItemCategory, byte>();
            if (ArmorBase.CategoryLimits != null)
                foreach (var limit in ArmorBase.CategoryLimits)
                    limits[limit.Category] = limit.Limit;
            return new ReadOnlyDictionary<ItemCategory, byte>(limits);
        }
    }
}
