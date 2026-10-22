using NativeProjectile = Il2CppInventorySystem.Items.ThrowableProjectiles.Scp2176Projectile;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only client wrapper for Scp2176Projectile.</summary>
public class Scp2176Projectile : TimedGrenadeProjectile
{
    internal Scp2176Projectile(NativeProjectile projectile) : base(projectile) => Scp2176Base = projectile;

    public NativeProjectile Scp2176Base { get; }
    /// <summary>Locally configured lockdown duration; the server owns affected rooms and actual duration.</summary>
    public float LockdownDuration => Scp2176Base.LockdownDuration;
}
