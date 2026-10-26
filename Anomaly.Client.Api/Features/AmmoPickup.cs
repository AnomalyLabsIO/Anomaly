using AmmoPickupBase = Il2CppInventorySystem.Items.Firearms.Ammo.AmmoPickup;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>A world ammunition stack with a replicated count.</summary>
[ScriptType]
public class AmmoPickup : Pickup
{
    internal AmmoPickup(AmmoPickupBase ammoBase) : base(ammoBase)
    {
        AmmoBase = ammoBase;
    }

    public AmmoPickupBase AmmoBase { get; }
    public ushort Ammo => AmmoBase.NetworkSavedAmmo;
    public int MaxAmmo => AmmoBase.MaxAmmo;
}
