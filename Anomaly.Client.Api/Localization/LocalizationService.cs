using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Anomaly.Client.Api.Internal.Localization;
using MelonLoader;
using Newtonsoft.Json;

namespace Anomaly.Client.Api.Localization;

/// <summary>
/// Public localization service. Loads translation files, resolves the active locale from the
/// SCPSL registry (authoritative) or config overrides, and supports registration of additional
/// namespaces by third-party mods.
///
/// Mod authors typically should not construct this directly — use <see cref="Tr.RegisterMod"/>.
/// </summary>
public class LocalizationService
{
    private readonly string _anomalyI18nDir;
    private readonly string _configPath;
    private readonly string _userDataDir;
    private readonly Assembly _resourceAssembly;
    private readonly Dictionary<string, string> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

    // Track registered namespaces for collision detection
    private readonly HashSet<string> _registeredNamespaces = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex NamespaceRegex =
        new("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$", RegexOptions.Compiled);

    public string ActiveLocale { get; private set; } = "en";

    /// <param name="resourceAssembly">
    /// The assembly that owns the embedded i18n resources to extract. Must be passed
    /// explicitly because <c>Assembly.GetExecutingAssembly()</c> here would return
    /// Anomaly.Client.Api, but the resources live in Anomaly.Client.
    /// </param>
    internal LocalizationService(string userDataDir, Assembly resourceAssembly)
    {
        _userDataDir = userDataDir;
        _resourceAssembly = resourceAssembly ?? throw new ArgumentNullException(nameof(resourceAssembly));
        _anomalyI18nDir = Path.Combine(userDataDir, "Anomaly", "i18n");
        _configPath = Path.Combine(userDataDir, "Anomaly", "config.json");

        EnsureAnomalyExtracted();
        ActiveLocale = ResolveLocale();
        LoadAnomalyTranslations();
    }

    /// <summary>
    /// Look up a translation key. Returns the key itself if missing.
    /// </summary>
    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (_active.TryGetValue(key, out var value)) return value;
        if (_fallback.TryGetValue(key, out var fb)) return fb;

        MelonLogger.Warning($"[Anomaly i18n] Missing key: {key}");
        return key;
    }

    /// <summary>
    /// Look up a translation key and format it with positional arguments ({0}, {1}, ...).
    /// </summary>
    public string Get(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>
    /// Register a third-party mod's translation directory. The mod owns a keyed namespace;
    /// all keys loaded from the provided directory are merged into the active and fallback tables.
    /// </summary>
    /// <param name="modNamespace">
    /// Lowercase dotted identifier (e.g., "mymod" or "cool.mod"). Cannot start with
    /// "anomaly." or "launcher." (reserved for the core platform).
    /// </param>
    /// <param name="i18nDirectoryPath">
    /// Absolute path to a directory containing per-locale JSON files (en.json, fr.json, ...).
    /// All keys in those files must be prefixed with the mod's namespace.
    /// </param>
    /// <returns>True if registered; false if rejected (invalid namespace or duplicate).</returns>
    public bool RegisterMod(string modNamespace, string i18nDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(modNamespace))
        {
            MelonLogger.Error("[Anomaly i18n] RegisterMod: namespace must not be empty.");
            return false;
        }

        modNamespace = modNamespace.ToLowerInvariant();

        if (!NamespaceRegex.IsMatch(modNamespace))
        {
            MelonLogger.Error($"[Anomaly i18n] RegisterMod: invalid namespace '{modNamespace}'. " +
                              "Must be lowercase alphanumeric with dots (e.g., 'mymod').");
            return false;
        }

        if (modNamespace.StartsWith("anomaly.", StringComparison.Ordinal)
            || modNamespace == "anomaly"
            || modNamespace.StartsWith("launcher.", StringComparison.Ordinal)
            || modNamespace == "launcher")
        {
            MelonLogger.Error($"[Anomaly i18n] RegisterMod: namespace '{modNamespace}' is reserved.");
            return false;
        }

        if (_registeredNamespaces.Contains(modNamespace))
        {
            MelonLogger.Warning($"[Anomaly i18n] RegisterMod: namespace '{modNamespace}' is already registered. Ignoring.");
            return false;
        }

        if (!Directory.Exists(i18nDirectoryPath))
        {
            MelonLogger.Error($"[Anomaly i18n] RegisterMod: directory '{i18nDirectoryPath}' does not exist.");
            return false;
        }

        try
        {
            MergeFileInto(_fallback, Path.Combine(i18nDirectoryPath, "en.json"));
            MergeActiveFromDir(i18nDirectoryPath);
            _registeredNamespaces.Add(modNamespace);
            MelonLogger.Msg($"[Anomaly i18n] Registered mod namespace '{modNamespace}' from '{i18nDirectoryPath}'.");
            return true;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[Anomaly i18n] RegisterMod '{modNamespace}' failed: {ex.Message}");
            return false;
        }
    }

    // ==================== Extraction ====================

    private void EnsureAnomalyExtracted()
    {
        try
        {
            if (!Directory.Exists(_anomalyI18nDir))
                Directory.CreateDirectory(_anomalyI18nDir);

            var assembly = _resourceAssembly;
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (resourceName.IndexOf(".Resources.i18n.", StringComparison.Ordinal) < 0) continue;
                if (!resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                var fileName = ExtractFileName(resourceName);
                var destPath = Path.Combine(_anomalyI18nDir, fileName);

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                // Hash both sides; only overwrite when the embedded payload differs from
                // what's on disk. This keeps user edits in place when the shipped file
                // hasn't changed, and forces re-extraction whenever a build ships new
                // translations (the assembly-version marker we used before missed
                // dev rebuilds that didn't bump the version).
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var embeddedBytes = ms.ToArray();

                if (File.Exists(destPath))
                {
                    var diskBytes = File.ReadAllBytes(destPath);
                    if (HashEquals(embeddedBytes, diskBytes))
                        continue;
                }

                File.WriteAllBytes(destPath, embeddedBytes);
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[Anomaly i18n] Extract failed: {ex.Message}");
        }
    }

    private static bool HashEquals(byte[] a, byte[] b)
    {
        using var sha = SHA256.Create();
        var ha = sha.ComputeHash(a);
        var hb = sha.ComputeHash(b);
        if (ha.Length != hb.Length) return false;
        for (int i = 0; i < ha.Length; i++)
            if (ha[i] != hb[i]) return false;
        return true;
    }

    private static string ExtractFileName(string resourceName)
    {
        const string marker = ".Resources.i18n.";
        var idx = resourceName.IndexOf(marker, StringComparison.Ordinal);
        return idx >= 0 ? resourceName.Substring(idx + marker.Length) : resourceName;
    }

    // ==================== Locale resolution ====================

    private string ResolveLocale()
    {
        // 1. SCPSL registry (authoritative)
        var scpslLocale = RegistryTxtReader.ReadLocale();
        if (!string.IsNullOrEmpty(scpslLocale))
            return scpslLocale;

        // 2. Anomaly config.json override
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonConvert.DeserializeObject<ClientConfig>(json);
                if (!string.IsNullOrEmpty(config?.Locale))
                    return config.Locale;
            }
            catch
            {
                // Ignore malformed config
            }
        }

        // 3. System culture
        var sys = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
        if (!string.IsNullOrEmpty(sys))
            return sys;

        return "en";
    }

    // ==================== Translation loading ====================

    private void LoadAnomalyTranslations()
    {
        MergeFileInto(_fallback, Path.Combine(_anomalyI18nDir, "en.json"));

        var exact = LoadJsonFile(Path.Combine(_anomalyI18nDir, $"{ActiveLocale}.json"));
        if (exact != null)
        {
            foreach (var kvp in exact) _active[kvp.Key] = kvp.Value;
            return;
        }

        var underscore = ActiveLocale.IndexOf('_');
        if (underscore > 0)
        {
            var prefix = ActiveLocale.Substring(0, underscore);
            var prefixMatch = LoadJsonFile(Path.Combine(_anomalyI18nDir, $"{prefix}.json"));
            if (prefixMatch != null)
            {
                foreach (var kvp in prefixMatch) _active[kvp.Key] = kvp.Value;
                return;
            }
        }

        // No active match — mirror fallback so Get() still finds English.
        foreach (var kvp in _fallback) _active[kvp.Key] = kvp.Value;
    }

    private void MergeActiveFromDir(string dir)
    {
        var exact = LoadJsonFile(Path.Combine(dir, $"{ActiveLocale}.json"));
        if (exact != null)
        {
            foreach (var kvp in exact) _active[kvp.Key] = kvp.Value;
            return;
        }

        var underscore = ActiveLocale.IndexOf('_');
        if (underscore > 0)
        {
            var prefix = ActiveLocale.Substring(0, underscore);
            var prefixMatch = LoadJsonFile(Path.Combine(dir, $"{prefix}.json"));
            if (prefixMatch != null)
            {
                foreach (var kvp in prefixMatch) _active[kvp.Key] = kvp.Value;
                return;
            }
        }

        // Nothing translated for active locale — fallback file (en.json) was already merged into _fallback.
    }

    private void MergeFileInto(Dictionary<string, string> target, string path)
    {
        var loaded = LoadJsonFile(path);
        if (loaded == null) return;
        foreach (var kvp in loaded)
            target[kvp.Key] = kvp.Value;
    }

    private static Dictionary<string, string> LoadJsonFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private class ClientConfig
    {
        [JsonProperty("locale")]
        public string Locale { get; set; }
    }
}
