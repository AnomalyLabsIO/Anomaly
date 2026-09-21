using System.Collections.Generic;

namespace Anomaly.Client.Api.UI.ModSettings;

/// <summary>
/// Optional, richer metadata for a single <c>MelonPreferences_Entry</c> rendered
/// in the in-game Mods settings tab.
/// </summary>
/// <remarks>
/// <para>
/// The Mods tab auto-discovers every loaded mod's typed <c>MelonPreferences</c>
/// entries and renders them with vanilla SCP:SL widgets based on the entry's
/// reflected type (bool → toggle, numeric+ValueRange → slider, plain numeric →
/// input field, enum → dropdown, string → input field). Pass an instance of
/// this class to
/// <see cref="ModSettings.RegisterEntry(MelonLoader.MelonPreferences_Entry, ModSettingMetadata)"/>
/// to override any of those heuristics for a specific entry.
/// </para>
/// <para>
/// All properties are nullable / opt-in. A null property means "fall back to the
/// MelonPreferences entry's own value (DisplayName, Description, Validator, etc.)".
/// </para>
/// </remarks>
public sealed class ModSettingMetadata
{
    /// <summary>Label shown next to the widget. Falls back to <c>entry.DisplayName</c>, then <c>entry.Identifier</c>.</summary>
    public string DisplayName { get; set; }

    /// <summary>Wrapped description beneath the setting label. Falls back to the entry's Description.</summary>
    public string Description { get; set; }

    /// <summary>Optional sub-header within the entry's category — adjacent entries sharing a non-null Group are visually grouped.</summary>
    public string Group { get; set; }

    /// <summary>Overrides numeric slider minimum. Wins over a <c>ValueRange</c> validator on the entry.</summary>
    public double? MinValue { get; set; }

    /// <summary>Overrides numeric slider maximum. Wins over a <c>ValueRange</c> validator on the entry.</summary>
    public double? MaxValue { get; set; }

    /// <summary>
    /// When non-null, forces a dropdown widget. Each string is shown verbatim
    /// as a dropdown option. For numeric entries, the selected index is written
    /// back as the value; for string entries, the option string itself is written.
    /// Enum choices must be declared enum names. Labels must be nonempty and unique.
    /// Choices cannot be combined with slider bounds. Invalid stored values remain unchanged
    /// until the user explicitly selects a valid choice or resets the setting.
    /// </summary>
    public IReadOnlyList<string> Choices { get; set; }

    /// <summary>Hide this entry from the Mods tab even if not <c>IsHidden</c> on the MelonPreferences entry itself.</summary>
    public bool Hidden { get; set; }

    /// <summary>Sort order within a category (ascending). Ties broken by display name. Default 0.</summary>
    public int Order { get; set; }
}
