using Il2CppScp914;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Read-only client-side facade over <see cref="Scp914Controller"/>. Exposes
/// the knob setting, upgrade-in-progress flag, and remaining cooldown for
/// HUD overlays / animation reskins / event hooks.
/// </summary>
/// <remarks>
/// LabAPI's server-side equivalent has methods like <c>Upgrade</c> and
/// <c>SetKnob</c> that mutate state. Those are intentionally absent here —
/// SCP-914 control is server-authoritative.
/// </remarks>
public static class Scp914
{
    /// <summary>The underlying <see cref="Scp914Controller"/> singleton, or null if not yet initialized.</summary>
    public static Scp914Controller Base => Scp914Controller.Singleton;

    /// <summary>True if the controller singleton is currently initialized.</summary>
    public static bool IsAvailable => Base != null;

    /// <summary>The current knob position (Rough / Coarse / OneToOne / Fine / VeryFine).</summary>
    public static Scp914KnobSetting KnobSetting => Base != null ? Base.KnobSetting : Scp914KnobSetting.OneToOne;

    /// <summary>True while a refinement cycle is running.</summary>
    public static bool IsUpgrading => Base != null && Base.IsUpgrading;

    /// <summary>Seconds remaining on the knob-change cooldown.</summary>
    public static float RemainingCooldown => Base != null ? Base.RemainingCooldown : 0f;

    /// <summary>Total wall-clock seconds for one full upgrade sequence (door close + upgrade + door open).</summary>
    public static float TotalSequenceTime => Base != null ? Base.TotalSequenceTime : 0f;
}
