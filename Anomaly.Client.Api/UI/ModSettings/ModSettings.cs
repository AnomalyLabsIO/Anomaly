using System;
using Anomaly.Client.Api.UI.ModSettings.Internal;
using MelonLoader;

namespace Anomaly.Client.Api.UI.ModSettings;

/// <summary>
/// Public façade for the in-game "Mods" settings tab. Third-party mods don't
/// need to call this at all — every typed <c>MelonPreferences</c> entry your
/// mod creates is auto-discovered and rendered with a sensible default widget
/// (bool → toggle, numeric+<c>ValueRange</c> → slider, plain numeric → input
/// field, enum → dropdown, string → input field).
/// </summary>
/// <remarks>
/// <para>
/// Use this API only when the auto-rendering needs richer information than
/// MelonPreferences alone provides — e.g., a custom dropdown over arbitrary
/// strings, an explicit min/max on a numeric that has no <c>ValueRange</c>,
/// or a custom section header.
/// </para>
/// <para>
/// Calls are safe at any point after MelonLoader has initialized your mod;
/// they update an internal registry and request the Mods tab to rebuild on
/// the next frame. Registration is idempotent — re-registering a category or
/// entry replaces the previous metadata.
/// </para>
/// </remarks>
public static class ModSettings
{
    /// <summary>
    /// Register or replace metadata for a <c>MelonPreferences_Category</c>
    /// rendered in the Mods tab.
    /// </summary>
    /// <param name="categoryId">The <c>MelonPreferences_Category.Identifier</c> value.</param>
    /// <param name="metadata">Override metadata, or <c>null</c> to clear any previous override.</param>
    public static void RegisterCategory(string categoryId, ModSettingCategoryMetadata metadata)
    {
        if (string.IsNullOrEmpty(categoryId))
            throw new ArgumentException("categoryId must be non-empty.", nameof(categoryId));

        ModSettingsRegistry.SetCategory(categoryId, metadata);
    }

    /// <summary>
    /// Register or replace metadata for a single <c>MelonPreferences_Entry</c>
    /// rendered in the Mods tab. The entry's <c>Category</c> must already be
    /// set (i.e., the entry must have been created via
    /// <c>category.CreateEntry(...)</c>).
    /// </summary>
    /// <param name="entry">The MelonPreferences entry whose UI should be customized.</param>
    /// <param name="metadata">Override metadata, or <c>null</c> to clear any previous override.</param>
    public static void RegisterEntry(MelonPreferences_Entry entry, ModSettingMetadata metadata)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        if (entry.Category == null)
            throw new ArgumentException(
                "Entry is not attached to a category. Create it via MelonPreferences_Category.CreateEntry(...).",
                nameof(entry));

        ModSettingValue.ValidateMetadata(entry.GetReflectedType(), metadata);
        ModSettingsRegistry.SetEntry(entry.Category.Identifier, entry.Identifier, metadata);
    }

    /// <summary>
    /// Request the Mods tab to rebuild its contents. Safe to call any time;
    /// the rebuild is coalesced and runs on the next Unity frame.
    /// </summary>
    public static void Refresh() => ModSettingsRegistry.RaiseChangedForRefresh();
}
