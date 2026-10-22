using NativeProjectile = Il2CppInventorySystem.Items.ThrowableProjectiles.FlashbangGrenade;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only client wrapper for FlashbangGrenade.</summary>
public class FlashbangProjectile : TimedGrenadeProjectile
{
    internal FlashbangProjectile(NativeProjectile projectile) : base(projectile) => FlashbangBase = projectile;

    public NativeProjectile FlashbangBase { get; }
    /// <summary>Locally configured blind duration, not a prediction of a player's received effect.</summary>
    public float BlindTime => FlashbangBase.BlindTime;
}
