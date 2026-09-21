using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Anomaly.Client.Api.Scheduling;
using Anomaly.Client.Api.UI.ModSettings.Internal;
using MelonLoader;

namespace Anomaly.Client.Patches.Menu.ModSettings;

/// <summary>Debounces mod preference saves and retains failed writes for explicit retry.</summary>
internal static class ModSettingsSaveScheduler
{
    private const int DebounceMs = 250;
    private const BindingFlags FileFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly PendingSettingsSaves<MelonPreferences_Category> _dirty = new();
    private static readonly Dictionary<MelonPreferences_Category, string> _errors = new();
    private static readonly HashSet<MelonPreferences_Category> _retryable = new();
    // The value records whether the loader watcher was active before this write failed.
    // Exact object identity prevents a replaced file inheriting another file's recovery permission.
    private static readonly Dictionary<object, bool> _writeFailures = new(ReferenceEqualityComparer.Instance);
    private static ScheduledTask _pending;
    internal static event Action<MelonPreferences_Category, string> SaveStatusChanged;

    internal static string GetError(MelonPreferences_Category category)
        => category != null && _errors.TryGetValue(category, out var error) ? error : null;

    internal static bool CanRetry(MelonPreferences_Category category)
        => category != null && _retryable.Contains(category);

    internal static void RequestSave(MelonPreferences_Category category)
    {
        if (category == null) return;
        _dirty.MarkDirty(category);
        _pending?.Cancel();
        _pending = Scheduler.In(TimeSpan.FromMilliseconds(DebounceMs), Flush, ownerId: "anomaly.modsettings.save");
    }

    /// <summary>Writes dirty categories, including an explicitly requested retry. Also called on app quit.</summary>
    internal static void Flush()
    {
        _pending?.Cancel();
        _pending = null;
        if (_dirty.Count == 0) return;

        _dirty.Flush(category =>
        {
            string error = SaveCategory(category, out bool canRetry);
            bool changed = !string.Equals(GetError(category), error, StringComparison.Ordinal)
                || CanRetry(category) != canRetry;
            if (error == null) _errors.Remove(category);
            else _errors[category] = error;
            if (canRetry) _retryable.Add(category);
            else _retryable.Remove(category);
            if (changed) SaveStatusChanged?.Invoke(category, error);
            return error == null;
        });
        // No periodic retry loop: ML logs every failed disk write. The user can fix
        // the cause and use Retry save; another setting edit also schedules a retry.
    }

    private static string SaveCategory(MelonPreferences_Category category, out bool canRetry)
    {
        canRetry = false;
        bool attemptedSave = false;
        object watcherSync = null;
        bool watcherLockHeld = false;
        try
        {
            object file = typeof(MelonPreferences_Category).GetField("File", FileFlags)?.GetValue(category)
                ?? typeof(MelonPreferences).GetField("DefaultFile", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            if (file == null)
                return "Changes are active, but preference storage is unavailable. See the log.";
            Type type = file.GetType();
            object currentWatcher = type.GetField("FileWatcher", FileFlags)?.GetValue(file);
            watcherSync = currentWatcher?.GetType().GetField("SyncLock", FileFlags)?.GetValue(currentWatcher) ?? file;
            // Match ML's watcher lock so a concurrent reload cannot turn a clean
            // file into a load-error latch in the middle of our save observation.
            Monitor.Enter(watcherSync, ref watcherLockHeld);
            var errorProperty = type.GetProperty("WasError", FileFlags);
            var shouldSaveField = type.GetField("ShouldSave", FileFlags);
            if (errorProperty?.PropertyType != typeof(bool) || !errorProperty.CanWrite || shouldSaveField == null)
                return "Changes are active, but this loader's save status could not be verified.";
            if (shouldSaveField.GetValue(file) is false)
                return "Changes are active for this session; this mod disabled preference-file saving.";

            bool wasError = (bool)errorProperty.GetValue(file);
            bool ownedFailure = _writeFailures.TryGetValue(file, out bool restoreWatcher);
            if (wasError && !ownedFailure)
                return "Changes are not saved. Repair the existing preference-file load error in the log, then restart.";

            bool recovering = wasError && ownedFailure;
            bool wasWatching = recovering ? restoreWatcher : HasActiveWatcher(file);
            if (wasError)
            {
                // ML 0.7.2 latches failed writes as well as corrupt loads. Clear only
                // the write latch observed by this scheduler on this exact File.
                errorProperty.SetValue(file, false);
            }
            else
            {
                // A previously recovered file gets no permission to clear a later,
                // independently raised load/corruption error.
                _writeFailures.Remove(file);
            }

            attemptedSave = true;
            try { category.SaveToFile(printmsg: false); }
            finally
            {
                if ((bool)errorProperty.GetValue(file)) _writeFailures[file] = wasWatching;
                else
                {
                    _writeFailures.Remove(file);
                    if (recovering && restoreWatcher)
                    {
                        // WasError=true destroys the watcher; setting it false does
                        // not recreate it. Restore only a watcher active before failure,
                        // even if a post-save subscriber subsequently threw.
                        try { RestoreWatcher(file); }
                        catch (Exception exception)
                        {
                            MelonLogger.Warning($"ModSettings saved '{category.Identifier}', but preference-file watching could not resume: {exception.Message}");
                        }
                    }
                }
            }
            if ((bool)errorProperty.GetValue(file))
            {
                canRetry = true;
                return "Changes are active but not saved. Fix the write error in the log, then select Retry save.";
            }

            return null;
        }
        catch (Exception exception)
        {
            canRetry = attemptedSave;
            if (!_errors.ContainsKey(category))
                MelonLogger.Error($"ModSettings save failed for '{category.Identifier}': {exception}");
            return canRetry
                ? "Changes are active but not saved. Fix the error in the log, then select Retry save."
                : "Changes are active, but preference storage could not be inspected safely. See the log and restart.";
        }
        finally
        {
            if (watcherLockHeld) Monitor.Exit(watcherSync);
        }
    }

    private static bool HasActiveWatcher(object file)
    {
        object watcher = file.GetType().GetField("FileWatcher", FileFlags)?.GetValue(file);
        return watcher?.GetType().GetField("FileWatcher", FileFlags)?.GetValue(watcher) != null;
    }

    private static void RestoreWatcher(object file)
    {
        var field = file.GetType().GetField("FileWatcher", FileFlags);
        if (field == null) return;
        // The successful recovery write happened while watching was disabled, so
        // its event cannot consume IsSaving. Do not ignore the next external edit.
        file.GetType().GetField("IsSaving", FileFlags)?.SetValue(file, false);
        object watcher = Activator.CreateInstance(field.FieldType, FileFlags, null, new[] { file }, null);
        field.SetValue(file, watcher);
    }
}
