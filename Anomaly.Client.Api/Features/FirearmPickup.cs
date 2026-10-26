using FirearmPickupBase = Il2CppInventorySystem.Items.Firearms.FirearmPickup;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around a world-spawned <see cref="FirearmPickupBase"/>. Adds a
/// typed downcast and the firearm-pickup-specific <see cref="IsTemplateSet"/>
/// flag on top of the base <see cref="Pickup"/> surface.
/// </summary>
/// <remarks>
/// Most useful firearm state (ammo count, fire mode, attachments) lives on the
/// <see cref="FirearmItem"/> wrapper when the firearm is in someone's
/// inventory. This wrapper is mainly an ergonomic downcast so consumers can
/// branch on "is this a firearm pickup?" without raw <c>is</c> checks.
/// </remarks>
[ScriptType]
public class FirearmPickup : Pickup
{
    internal FirearmPickup(FirearmPickupBase firearmPickupBase) : base(firearmPickupBase)
    {
        FirearmPickupBase = firearmPickupBase;
    }

    /// <summary>The underlying <see cref="FirearmPickupBase"/>.</summary>
    public FirearmPickupBase FirearmPickupBase { get; }

    /// <summary>
    /// True once the firearm template (attachments, modules) has been resolved
    /// for this pickup. Mods that read attachment state should wait until this
    /// is true.
    /// </summary>
    public bool IsTemplateSet => FirearmPickupBase != null && FirearmPickupBase._templateSet;

    public override string ToString() => $"FirearmPickup({TypeName}, {Serial})";
}
