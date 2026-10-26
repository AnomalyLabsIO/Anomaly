using TimedGrenadePickupBase = Il2CppInventorySystem.Items.ThrowableProjectiles.TimedGrenadePickup;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around a world-spawned <see cref="TimedGrenadePickupBase"/> — a
/// dropped (un-thrown) grenade that's still in its idle state. Once thrown,
/// grenades become <see cref="Projectile"/>s instead.
/// </summary>
/// <remarks>
/// Implements <c>IExplosionTrigger</c> on the engine side — this pickup will
/// detonate (transition to a thrown projectile) if it's in proximity to
/// another explosion (the "chain reaction" behaviour).
/// </remarks>
[ScriptType]
public class TimedGrenadePickup : Pickup
{
    internal TimedGrenadePickup(TimedGrenadePickupBase grenadePickupBase) : base(grenadePickupBase)
    {
        TimedGrenadePickupBase = grenadePickupBase;
    }

    /// <summary>The underlying <see cref="TimedGrenadePickupBase"/>.</summary>
    public TimedGrenadePickupBase TimedGrenadePickupBase { get; }

    /// <summary>
    /// True for one frame when the engine is about to swap this pickup with
    /// the corresponding <see cref="Projectile"/> (the "live grenade" trigger).
    /// </summary>
    public bool IsAboutToReplace =>
        TimedGrenadePickupBase != null && TimedGrenadePickupBase._replaceNextFrame;

    /// <summary>Range within which a nearby explosion will chain-detonate this grenade.</summary>
    public static float ChainActivationRange => TimedGrenadePickupBase.ActivationRange;

    public override string ToString() => $"TimedGrenadePickup({TypeName}, {Serial})";
}
