using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Client.API.Persistence;
using Anomaly.Client.API.Scheduling;
using MelonLoader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Anomaly.Client.Internal.Persistence;

/// <summary>
/// JSON-file-backed <see cref="IPersistenceStore"/> with debounced writes.
/// Values are held as <see cref="JToken"/> in memory and (de)serialized on
/// demand, matching <c>JsonConfigSource</c>'s approach.
/// </summary>
internal sealed class JsonPersistenceStore : IPersistenceStore
{
    private static readonly TimeSpan WriteDebounce = TimeSpan.FromMilliseconds(500);

    private readonly string _path;
    private readonly Dictionary<string, JToken> _values = new Dictionary<string, JToken>(StringComparer.Ordinal);
    private ScheduledTask _pendingWrite;

    public string ModId { get; }

    internal JsonPersistenceStore(string modId, string path)
    {
        ModId = modId;
        _path = path;
        Load();
    }

    public T Load<T>(string key, T defaultValue = default)
    {
        if (string.IsNullOrEmpty(key) || !_values.TryGetValue(key, out var token) || token == null)
            return defaultValue;

        try { return token.ToObject<T>(); }
        catch (Exception ex)
        {
            MelonLogger.Warning($"JsonPersistenceStore[{ModId}]: Load<{typeof(T).Name}>('{key}') failed: {ex.Message}");
            return defaultValue;
        }
    }

    public void Save<T>(string key, T value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Persistence key must be non-empty.", nameof(key));

        var newToken = value == null ? JValue.CreateNull() : JToken.FromObject(value);

        _values.TryGetValue(key, out var existing);
        if (existing != null && JToken.DeepEquals(existing, newToken)) return;

        _values[key] = newToken;
        SchedulePendingWrite();
    }

    public bool Exists(string key) => !string.IsNullOrEmpty(key) && _values.ContainsKey(key);

    public void Delete(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (_values.Remove(key)) SchedulePendingWrite();
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
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var obj = new JObject();
            foreach (var kvp in _values) obj[kvp.Key] = kvp.Value;

            File.WriteAllText(_path, obj.ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"JsonPersistenceStore[{ModId}]: write failed: {ex}");
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            string text = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(text)) return;

            var obj = JObject.Parse(text);
            foreach (var pair in obj)
                _values[pair.Key] = pair.Value;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"JsonPersistenceStore[{ModId}]: load failed for '{_path}': {ex.Message}");
        }
    }
}
