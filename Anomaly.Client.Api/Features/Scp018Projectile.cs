using NativeProjectile = Il2CppInventorySystem.Items.ThrowableProjectiles.Scp018Projectile;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only client wrapper for Scp018Projectile.</summary>
public class Scp018Projectile : TimedGrenadeProjectile
{
    internal Scp018Projectile(NativeProjectile projectile) : base(projectile) => Scp018Base = projectile;

    public NativeProjectile Scp018Base { get; }
    public UnityEngine.Vector3 RecreatedVelocity => Scp018Base.RecreatedVelocity;
}
