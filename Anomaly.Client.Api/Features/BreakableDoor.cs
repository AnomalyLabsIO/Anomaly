using Il2CppInteractables.Interobjects.DoorUtils;
using BreakableDoorBase = Il2CppInteractables.Interobjects.BreakableDoor;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="BreakableDoorBase"/>. Adds health / damage-type
/// state on top of the base <see cref="Door"/> surface for HUD overlays
/// (door HP bars, "this door is about to break" warnings, faction-specific
/// damage indicators).
/// </summary>
public class BreakableDoor : Door
{
    internal BreakableDoor(BreakableDoorBase doorBase) : base(doorBase)
    {
        BreakableBase = doorBase;
    }

    /// <summary>The underlying <see cref="BreakableDoorBase"/>.</summary>
    public BreakableDoorBase BreakableBase { get; }

    /// <summary>Current door HP.</summary>
    public float Health => BreakableBase != null ? BreakableBase.RemainingHealth : 0f;

    /// <summary>Maximum door HP (the value <see cref="Health"/> regenerates / starts at).</summary>
    public float MaxHealth => BreakableBase != null ? BreakableBase.MaxHealth : 0f;

    /// <summary>Health as a 0..1 fraction of <see cref="MaxHealth"/>. Returns 0 if max is zero.</summary>
    public float HealthPercent =>
        BreakableBase != null && BreakableBase.MaxHealth > 0f
            ? BreakableBase.GetHealthPercent()
            : 0f;

    /// <summary>Damage types that this door is immune to.</summary>
    public DoorDamageType IgnoredDamageSources =>
        BreakableBase != null ? BreakableBase.IgnoredDamageSources : DoorDamageType.None;

    /// <summary>True if the door has been broken (health depleted).</summary>
    public bool IsBroken => Health <= 0f;
}
