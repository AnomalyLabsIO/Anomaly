using System;
using System.Collections.Generic;
using System.Text;
using Anomaly.Client.Api.Persistence;
using Anomaly.Client.Api.Scheduling;
using MelonLoader;
using Newtonsoft.Json;

namespace Anomaly.Client.Api.Internal.Persistence;

/// <summary>
/// MelonPreferences-backed <see cref="IPersistenceStore"/> with debounced
/// writes. Values are JSON strings inside MelonPreferences entries so callers
/// can continue storing arbitrary Json.NET-serializable values.
/// </summary>
internal sealed class MelonPreferencesPersistenceStore : IPersistenceStore
{
    private static readonly TimeSpan WriteDebounce = TimeSpan.FromMilliseconds(500);

    private readonly MelonPreferences_Category _category;
    private readonly Dictionary<string, MelonPreferences_Entry<string>> _entries =
        new Dictionary<string, MelonPreferences_Entry<string>>(StringComparer.Ordinal);
    private ScheduledTask _pendingWrite;

    public string ModId { get; }

    internal MelonPreferencesPersistenceStore(string modId)
    {
        ModId = modId;
        _category = MelonPreferences.CreateCategory(
            CategoryId("persistence", modId),
            $"Anomaly Persistence: {modId}");
    }

    public T Load<T>(string key, T defaultValue = default)
    {
        if (string.IsNullOrEmpty(key))
            return defaultValue;

        var entry = GetOrCreateEntry(key);
        string json = entry.Value;
        if (string.IsNullOrEmpty(json))
            return defaultValue;

        try { return JsonConvert.DeserializeObject<T>(json); }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MelonPreferencesPersistenceStore[{ModId}]: Load<{typeof(T).Name}>('{key}') failed: {ex.Message}");
            return defaultValue;
        }
    }

    public void Save<T>(string key, T value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Persistence key must be non-empty.", nameof(key));

        string json = JsonConvert.SerializeObject(value);
        var entry = GetOrCreateEntry(key);
        if (string.Equals(entry.Value, json, StringComparison.Ordinal)) return;

        entry.Value = json;
        SchedulePendingWrite();
    }

    public bool Exists(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        var entry = GetOrCreateEntry(key);
        return !string.IsNullOrEmpty(entry.Value);
    }

    public void Delete(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        string entryId = EntryId(key);
        if (_category.DeleteEntry(entryId))
        {
            _entries.Remove(key);
            SchedulePendingWrite();
        }
    }

    public void Flush()
    {
        _pendingWrite?.Cancel();
        _pendingWrite = null;
        WriteToDisk();
    }

    private void SchedulePendingWrite()
    {
        _pendingWrite?.Cancel();
        _pendingWrite = Scheduler.In(WriteDebounce, WriteToDisk, ownerId: $"anomaly.persistence.{ModId}");
    }

    private void WriteToDisk()
    {
        try
        {
            _category.SaveToFile(printmsg: false);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"MelonPreferencesPersistenceStore[{ModId}]: write failed: {ex}");
        }
    }

    private MelonPreferences_Entry<string> GetOrCreateEntry(string key)
    {
        if (_entries.TryGetValue(key, out var existing))
            return existing;

        string entryId = EntryId(key);
        var entry = _category.GetEntry<string>(entryId);
        if (entry == null)
            entry = _category.CreateEntry(entryId, default_value: (string)null, display_name: key);

        entry.Description = $"Anomaly.Client.Api ClientPersistence key '{key}' for '{ModId}'. Value is JSON.";
        _entries[key] = entry;
        return entry;
    }

    private static string CategoryId(string kind, string modId) => $"anomaly_{kind}_{Hex(modId)}";

    private static string EntryId(string key) => $"k_{Hex(key)}";

    private static string Hex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
