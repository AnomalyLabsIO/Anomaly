using ThrowableItemBase = Il2CppInventorySystem.Items.ThrowableProjectiles.ThrowableItem;
using ProjectileSettings = Il2CppInventorySystem.Items.ThrowableProjectiles.ThrowableItem.ProjectileSettings;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="ThrowableItemBase"/>. Exposes the weak/full throw
/// settings (force, fuse) for HUD overlays (throw-strength indicator,
/// fuse-time tooltips, custom telemetry).
/// </summary>
public class ThrowableItem : Item
{
    internal ThrowableItem(ThrowableItemBase throwableBase) : base(throwableBase)
    {
        ThrowableBase = throwableBase;
    }

    /// <summary>The underlying <see cref="ThrowableItemBase"/>.</summary>
    public ThrowableItemBase ThrowableBase { get; }

    /// <summary>Trajectory settings for a tap-to-throw (weak) toss. Null if base is destroyed.</summary>
    public ProjectileSettings? WeakThrowSettings => ThrowableBase?.WeakThrowSettings;

    /// <summary>Trajectory settings for a hold-to-throw (full power) toss. Null if base is destroyed.</summary>
    public ProjectileSettings? FullThrowSettings => ThrowableBase?.FullThrowSettings;

    public override string ToString() => $"ThrowableItem({TypeName}, {Serial})";
}
