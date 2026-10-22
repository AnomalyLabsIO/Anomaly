using ArmorPickupBase = Il2CppInventorySystem.Items.Armor.BodyArmorPickup;

namespace Anomaly.Client.Api.Features;

/// <summary>A typed world armor pickup. Inventory armor configuration is exposed by <see cref="BodyArmorItem"/>.</summary>
public class BodyArmorPickup : Pickup
{
    internal BodyArmorPickup(ArmorPickupBase armorBase) : base(armorBase)
    {
        ArmorBase = armorBase;
    }

    public ArmorPickupBase ArmorBase { get; }
}
