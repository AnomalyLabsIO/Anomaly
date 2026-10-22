using NativeProjectile = Il2CppInventorySystem.Items.ThrowableProjectiles.ExplosionGrenade;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only client wrapper for ExplosionGrenade.</summary>
public class ExplosiveGrenadeProjectile : TimedGrenadeProjectile
{
    internal ExplosiveGrenadeProjectile(NativeProjectile projectile) : base(projectile) => ExplosiveBase = projectile;

    public NativeProjectile ExplosiveBase { get; }
    /// <summary>Locally configured radius; server plugin overrides are not implied.</summary>
    public float MaxRadius => ExplosiveBase.MaxRadius;
}
