using System;
using System.Collections.Generic;
using System.Text;
using Anomaly.Client.Api.Config;
using Anomaly.Client.Api.Events;
using MelonLoader;
using Newtonsoft.Json;

namespace Anomaly.Client.Api.Internal.Config;

/// <summary>
/// MelonPreferences-backed <see cref="IConfigSource"/>. Values are stored as
/// JSON strings inside MelonPreferences entries so the compatibility API can
/// keep accepting arbitrary Json.NET-serializable values using MelonLoader's
/// existing preferences storage.
/// </summary>
internal sealed class MelonPreferencesConfigSource : IConfigSource
{
    private readonly MelonPreferences_Category _category;
    private readonly Dictionary<string, MelonPreferences_Entry<string>> _entries =
        new Dictionary<string, MelonPreferences_Entry<string>>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _entryIdsToKeys =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string ModId { get; }

    public event Action<string> KeyChanged;

    internal MelonPreferencesConfigSource(string modId)
    {
        ModId = modId;
        _category = MelonPreferences.CreateCategory(
            CategoryId("config", modId),
            $"Anomaly Config: {modId}");
    }

    public T Get<T>(string key, T defaultValue = default)
    {
        if (string.IsNullOrEmpty(key))
            return defaultValue;

        var entry = GetOrCreateEntry(key);
        string json = entry.Value;
        if (string.IsNullOrEmpty(json))
            return defaultValue;

        try
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MelonPreferencesConfigSource[{ModId}]: Get<{typeof(T).Name}>('{key}') failed: {ex.Message}");
            return defaultValue;
        }
    }

    public void Set<T>(string key, T value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Config key must be non-empty.", nameof(key));

        string json = JsonConvert.SerializeObject(value);
        var entry = GetOrCreateEntry(key);
        if (string.Equals(entry.Value, json, StringComparison.Ordinal)) return;

        entry.Value = json;
        FireKeyChanged(key);
    }

    public bool Has(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        var entry = GetOrCreateEntry(key);
        return !string.IsNullOrEmpty(entry.Value);
    }

    public void Remove(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        string entryId = EntryId(key);
        if (_category.DeleteEntry(entryId))
        {
            _entries.Remove(key);
            _entryIdsToKeys.Remove(entryId);
            FireKeyChanged(key);
        }
    }

    public void Save() => _category.SaveToFile(printmsg: false);

    public void Reload()
    {
        var previous = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in _entries)
            previous[kvp.Key] = kvp.Value.Value;

        _category.LoadFromFile(printmsg: false);

        foreach (var kvp in _entries)
        {
            previous.TryGetValue(kvp.Key, out var prev);
            if (!string.Equals(prev, kvp.Value.Value, StringComparison.Ordinal))
                FireKeyChanged(kvp.Key);
        }
    }

    private void FireKeyChanged(string key)
    {
        EventInvoker.Invoke(
            KeyChanged,
            key,
            $"MelonPreferencesConfigSource[{ModId}].KeyChanged('{key}')");
    }

    private MelonPreferences_Entry<string> GetOrCreateEntry(string key)
    {
        if (_entries.TryGetValue(key, out var existing))
            return existing;

        string entryId = EntryId(key);
        var entry = _category.GetEntry<string>(entryId);
        if (entry == null)
            entry = _category.CreateEntry(entryId, default_value: (string)null, display_name: key);

        entry.Description = $"Anomaly.Client.Api ClientConfig key '{key}' for '{ModId}'. Value is JSON.";
        _entries[key] = entry;
        _entryIdsToKeys[entryId] = key;
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
