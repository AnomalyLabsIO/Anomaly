using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Client.Api.Input;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Anomaly.Client.Api.Internal.Input;

/// <summary>
/// JSON-file-backed storage for user input overrides. Reads once at Anomaly
/// startup and writes on every rebind. The file lives at
/// <c>&lt;UserData&gt;/Anomaly/bindings.json</c>.
/// </summary>
/// <remarks>
/// <para>File format (illustrative):</para>
/// <code>
/// {
///   "acme.wargames.fire_flare": { "key": "F", "modifiers": ["Shift"] },
///   "acme.wargames.toggle_hud": { "key": "H", "modifiers": [] }
/// }
/// </code>
/// <para>
/// Overrides are loaded lazily on first access so that plugin registrations
/// occurring during MelonMod initialization (before Anomaly bootstrap finishes)
/// still pick up user overrides.
/// </para>
/// </remarks>
internal static class InputConfigLoader
{
    private static Dictionary<string, SerializedBinding> _overrides;
    private static bool _loaded;

    private static string PathToFile =>
        Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly", "bindings.json");

    internal static bool TryGetOverride(string id, out InputKey key)
    {
        EnsureLoaded();
        if (_overrides.TryGetValue(id, out var serialized) && serialized.TryToInputKey(out key))
            return true;
        key = default;
        return false;
    }

    internal static void SetOverride(string id, InputKey key)
    {
        EnsureLoaded();
        _overrides[id] = SerializedBinding.From(key);
        Save();
    }

    internal static void RemoveOverride(string id)
    {
        EnsureLoaded();
        if (_overrides.Remove(id)) Save();
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        _overrides = new Dictionary<string, SerializedBinding>(StringComparer.Ordinal);

        try
        {
            string path = PathToFile;
            if (!File.Exists(path)) return;

            string json = File.ReadAllText(path);
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, SerializedBinding>>(json);
            if (parsed != null)
            {
                foreach (var kvp in parsed)
                    _overrides[kvp.Key] = kvp.Value;
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"InputConfigLoader: failed to load bindings.json: {ex.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            string path = PathToFile;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(_overrides, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"InputConfigLoader: failed to save bindings.json: {ex.Message}");
        }
    }

    /// <summary>JSON DTO for a single binding override.</summary>
    private class SerializedBinding
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("modifiers")]
        public List<string> Modifiers { get; set; } = new List<string>();

        internal static SerializedBinding From(InputKey key)
        {
            var mods = new List<string>();
            if (key.HasModifier(InputModifiers.Ctrl))  mods.Add("Ctrl");
            if (key.HasModifier(InputModifiers.Shift)) mods.Add("Shift");
            if (key.HasModifier(InputModifiers.Alt))   mods.Add("Alt");
            return new SerializedBinding
            {
                Key = key.Key.ToString(),
                Modifiers = mods
            };
        }

        internal bool TryToInputKey(out InputKey key)
        {
            key = default;
            if (string.IsNullOrEmpty(Key)) return false;
            if (!System.Enum.TryParse<UnityEngine.KeyCode>(Key, ignoreCase: true, out var code))
                return false;

            var mods = InputModifiers.None;
            if (Modifiers != null)
            {
                foreach (var m in Modifiers)
                {
                    switch ((m ?? string.Empty).Trim().ToLowerInvariant())
                    {
                        case "ctrl":
                        case "control": mods |= InputModifiers.Ctrl; break;
                        case "shift": mods |= InputModifiers.Shift; break;
                        case "alt": mods |= InputModifiers.Alt; break;
                    }
                }
            }

            key = new InputKey(code, mods);
            return true;
        }
    }
}
