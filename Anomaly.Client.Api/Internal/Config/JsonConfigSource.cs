using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Client.Api.Config;
using MelonLoader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Anomaly.Client.Api.Internal.Config;

/// <summary>
/// Newtonsoft.Json-backed <see cref="IConfigSource"/>. Values are stored as
/// <see cref="JToken"/> in-memory and (de)serialized on demand, so Get&lt;T&gt;
/// works with arbitrary POCOs without baking the schema into the store.
/// </summary>
/// <remarks>
/// Implements <see cref="IDisposable"/> so <see cref="ClientConfig.DisposeAll"/>
/// can release the underlying <see cref="ConfigFileWatcher"/> on shutdown.
/// </remarks>
internal sealed class JsonConfigSource : IConfigSource, IDisposable
{
    private readonly string _path;
    private readonly Dictionary<string, JToken> _values = new Dictionary<string, JToken>(StringComparer.Ordinal);
    private ConfigFileWatcher _watcher;
    private bool _disposed;

    public string ModId { get; }

    public event Action<string> KeyChanged;

    internal JsonConfigSource(string modId, string path)
    {
        ModId = modId;
        _path = path;
        Load();
        _watcher = new ConfigFileWatcher(_path, OnFileChanged);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var w = _watcher;
        _watcher = null;
        w?.Dispose();
    }

    public T Get<T>(string key, T defaultValue = default)
    {
        if (string.IsNullOrEmpty(key) || !_values.TryGetValue(key, out var token) || token == null)
            return defaultValue;

        try
        {
            return token.ToObject<T>();
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"JsonConfigSource[{ModId}]: Get<{typeof(T).Name}>('{key}') failed: {ex.Message}");
            return defaultValue;
        }
    }

    public void Set<T>(string key, T value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Config key must be non-empty.", nameof(key));

        var newToken = value == null ? JValue.CreateNull() : JToken.FromObject(value);

        _values.TryGetValue(key, out var existing);
        if (existing != null && JToken.DeepEquals(existing, newToken)) return;

        _values[key] = newToken;
        try { KeyChanged?.Invoke(key); }
        catch (Exception ex) { MelonLogger.Error($"JsonConfigSource[{ModId}]: KeyChanged handler threw for '{key}': {ex}"); }
    }

    public bool Has(string key) => !string.IsNullOrEmpty(key) && _values.ContainsKey(key);

    public void Remove(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (_values.Remove(key))
        {
            try { KeyChanged?.Invoke(key); }
            catch (Exception ex) { MelonLogger.Error($"JsonConfigSource[{ModId}]: KeyChanged handler threw for '{key}': {ex}"); }
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Serialize as JObject for stable pretty-printed output.
            var obj = new JObject();
            foreach (var kvp in _values) obj[kvp.Key] = kvp.Value;

            // Suppress the file-watcher during our own write so we don't re-trigger.
            _watcher?.SuppressNextChange();

            File.WriteAllText(_path, obj.ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"JsonConfigSource[{ModId}]: save failed: {ex}");
        }
    }

    public void Reload()
    {
        var previous = new Dictionary<string, JToken>(_values, StringComparer.Ordinal);
        _values.Clear();
        Load();

        // Fire KeyChanged for every key whose new value differs from the previous value.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in _values)
        {
            seen.Add(kvp.Key);
            previous.TryGetValue(kvp.Key, out var prev);
            if (!JToken.DeepEquals(prev, kvp.Value))
                FireKeyChanged(kvp.Key);
        }
        foreach (var kvp in previous)
        {
            if (!seen.Contains(kvp.Key))
                FireKeyChanged(kvp.Key);
        }
    }

    private void FireKeyChanged(string key)
    {
        try { KeyChanged?.Invoke(key); }
        catch (Exception ex) { MelonLogger.Error($"JsonConfigSource[{ModId}]: KeyChanged handler threw for '{key}': {ex}"); }
    }

    private void OnFileChanged()
    {
        Reload();
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
            MelonLogger.Warning($"JsonConfigSource[{ModId}]: load failed for '{_path}': {ex.Message}");
        }
    }
}
