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

    public new static IReadOnlyCollection<BreakableDoor> List => Door.List.OfType<BreakableDoor>().ToArray();
    public static BreakableDoor Get(BreakableDoorBase value) => Door.Get(value) as BreakableDoor;
    public static bool TryGet(BreakableDoorBase value, out BreakableDoor door) => (door = Get(value)) != null;

    /// <summary>Locally-held door HP. The game does not replicate damage HP, so remote clients cannot treat this as authoritative.</summary>
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

    /// <summary>The replicated broken state, independent of unavailable server health.</summary>
    public bool IsBroken => BreakableBase != null && BreakableBase.Network_destroyed;
}
