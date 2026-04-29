using Il2CppLightContainmentZoneDecontamination;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Read-only client-side facade over <see cref="DecontaminationController"/>.
/// Exposes the in-progress flag and announcement-hearable state for HUD
/// overlays (decon imminent warnings, fog-buildup vignettes, audio mods).
/// </summary>
/// <remarks>
/// Decontamination triggering is server-authoritative; this wrapper is
/// observation-only.
/// </remarks>
public static class Decontamination
{
    /// <summary>The underlying <see cref="DecontaminationController"/> singleton, or null if not yet initialized.</summary>
    public static DecontaminationController Base => DecontaminationController.Singleton;

    /// <summary>True if the controller singleton is currently initialized.</summary>
    public static bool IsAvailable => Base != null;

    /// <summary>True while LCZ decontamination is in its active gas phase.</summary>
    public static bool IsDecontaminating => Base != null && Base.IsDecontaminating;

    /// <summary>True while a CASSIE announcement about decontamination would be audible to the local player.</summary>
    public static bool IsAnnouncementHearable => Base != null && Base.IsAnnouncementHearable;
}
