using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Scheduling;
using MelonLoader;

namespace Anomaly.Client.Patches.Menu.ModSettings;

/// <summary>
/// Debounces <see cref="MelonPreferences_Category.SaveToFile(bool)"/> calls so
/// dragging a slider or rapidly toggling a setting in the Mods tab produces a
/// single disk write instead of one per frame.
/// </summary>
internal static class ModSettingsSaveScheduler
{
    private const int DebounceMs = 250;

    private static readonly HashSet<MelonPreferences_Category> _dirty = new();
    private static ScheduledTask _pending;

    internal static void RequestSave(MelonPreferences_Category category)
    {
        if (category == null) return;
        _dirty.Add(category);
        _pending?.Cancel();
        _pending = Scheduler.In(
            TimeSpan.FromMilliseconds(DebounceMs),
            Flush,
            ownerId: "anomaly.modsettings.save");
    }

    /// <summary>Immediately writes all dirty categories. Called on app quit.</summary>
    internal static void Flush()
    {
        _pending = null;
        if (_dirty.Count == 0) return;

        foreach (var c in _dirty)
        {
            try { c.SaveToFile(printmsg: false); }
            catch (Exception ex) { MelonLogger.Error($"ModSettings save failed for '{c?.Identifier}': {ex}"); }
        }
        _dirty.Clear();
    }
}
