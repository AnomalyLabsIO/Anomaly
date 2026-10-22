using NativeProjectile = Il2CppInventorySystem.Items.ThrowableProjectiles.TimeGrenade;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only client wrapper for TimeGrenade.</summary>
public class TimedGrenadeProjectile : Projectile
{
    internal TimedGrenadeProjectile(NativeProjectile projectile) : base(projectile) => TimedGrenadeBase = projectile;

    public NativeProjectile TimedGrenadeBase { get; }
    /// <summary>Replicated fuse deadline in Mirror network time; null until a fuse is scheduled.</summary>
    public double? DetonationTime => !IsDestroyed && TimedGrenadeBase.TargetTime > 0d ? TimedGrenadeBase.TargetTime : null;
    /// <summary>Client estimate only; the server owns actual detonation.</summary>
    public double? RemainingFuse => DetonationTime is double deadline ? System.Math.Max(0d, deadline - Il2CppMirror.NetworkTime.time) : null;
}
