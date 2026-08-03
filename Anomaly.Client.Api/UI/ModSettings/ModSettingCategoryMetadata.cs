namespace Anomaly.Client.Api.UI.ModSettings;

/// <summary>
/// Optional override metadata for a <c>MelonPreferences_Category</c> displayed
/// as a section in the Mods settings tab. Register via
/// <see cref="ModSettings.RegisterCategory(string, ModSettingCategoryMetadata)"/>.
/// </summary>
public sealed class ModSettingCategoryMetadata
{
    /// <summary>Section header text. Falls back to <c>category.DisplayName</c>, then <c>category.Identifier</c>.</summary>
    public string DisplayName { get; set; }

    /// <summary>Ascending sort order across categories in the Mods tab. Default 0. Negative values appear first.</summary>
    public int SortOrder { get; set; }

    /// <summary>Hide the whole category from the Mods tab.</summary>
    public bool Hidden { get; set; }
}
