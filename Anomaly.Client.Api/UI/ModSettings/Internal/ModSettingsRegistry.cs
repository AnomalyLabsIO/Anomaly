using System;
using System.Collections.Generic;

namespace Anomaly.Client.Api.UI.ModSettings.Internal;

/// <summary>
/// In-memory store for opt-in metadata supplied via the public
/// <see cref="ModSettings"/> facade. Read by the Anomaly.Client-side renderer
/// to override the auto-discovered display of each MelonPreferences entry.
/// </summary>
/// <remarks>
/// Public to <c>Anomaly.Client</c> (the only consumer of the renderer) via
/// <c>InternalsVisibleTo</c>. Third-party mods touch this only indirectly
/// through the <see cref="ModSettings"/> facade.
/// </remarks>
internal static class ModSettingsRegistry
{
    private static readonly object _lock = new();
    private static readonly Dictionary<string, ModSettingCategoryMetadata> _categories =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<(string CategoryId, string EntryId), ModSettingMetadata> _entries =
        new();

    /// <summary>Bumped whenever the registry changes — the renderer reads this to decide whether to rebuild.</summary>
    internal static int Revision { get; private set; }

    /// <summary>Hook installed by Anomaly.Client to forward registrations to a pending rebuild.</summary>
    internal static event Action OnChanged;

    internal static void SetCategory(string categoryId, ModSettingCategoryMetadata metadata)
    {
        if (string.IsNullOrEmpty(categoryId)) return;
        lock (_lock)
        {
            if (metadata == null) _categories.Remove(categoryId);
            else _categories[categoryId] = metadata;
            Revision++;
        }
        OnChanged?.Invoke();
    }

    internal static void SetEntry(string categoryId, string entryId, ModSettingMetadata metadata)
    {
        if (string.IsNullOrEmpty(categoryId) || string.IsNullOrEmpty(entryId)) return;
        lock (_lock)
        {
            var key = (categoryId, entryId);
            if (metadata == null) _entries.Remove(key);
            else _entries[key] = metadata;
            Revision++;
        }
        OnChanged?.Invoke();
    }

    internal static ModSettingCategoryMetadata GetCategory(string categoryId)
    {
        if (string.IsNullOrEmpty(categoryId)) return null;
        lock (_lock)
        {
            _categories.TryGetValue(categoryId, out var meta);
            return meta;
        }
    }

    internal static ModSettingMetadata GetEntry(string categoryId, string entryId)
    {
        if (string.IsNullOrEmpty(categoryId) || string.IsNullOrEmpty(entryId)) return null;
        lock (_lock)
        {
            _entries.TryGetValue((categoryId, entryId), out var meta);
            return meta;
        }
    }

    internal static bool IsCategoryHidden(string categoryId)
        => GetCategory(categoryId)?.Hidden == true;

    internal static bool IsEntryHidden(string categoryId, string entryId)
        => GetEntry(categoryId, entryId)?.Hidden == true;

    /// <summary>
    /// Bumps the revision and fires <see cref="OnChanged"/> without registering any
    /// new metadata. Used by <see cref="ModSettings.Refresh"/> to request a rebuild
    /// after a mod has updated state the renderer can't observe directly (e.g., a
    /// new MelonPreferences entry that should now appear in the tab).
    /// </summary>
    internal static void RaiseChangedForRefresh()
    {
        lock (_lock) { Revision++; }
        OnChanged?.Invoke();
    }
}
