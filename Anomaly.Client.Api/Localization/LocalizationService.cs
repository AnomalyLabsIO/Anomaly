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
using YamlDotNet.Serialization;

namespace Anomaly.Client.Api.Localization;

/// <summary>
/// Public localization service. Loads translation files, resolves the active locale from the
/// SCPSL registry (authoritative) or config overrides, and supports registration of additional
/// namespaces by third-party mods.
///
/// Mod authors typically should not construct this directly — use <see cref="Tr.RegisterMod(string)"/>.
/// </summary>
public class LocalizationService
{
    private readonly string _anomalyI18nDir;
    private readonly string _userDataDir;
    private readonly Assembly _resourceAssembly;
    private readonly MelonPreferences_Entry<string> _localeOverride;
    private Dictionary<string, string> _active = new(StringComparer.Ordinal);
    private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

    // Track registered namespaces for collision detection
    private HashSet<string> _registeredNamespaces = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex NamespaceRegex =
        new("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$", RegexOptions.Compiled);

    public string ActiveLocale { get; private set; } = "en";

    /// <param name="userDataDir">Root directory for Anomaly's extracted localization data.</param>
    /// <param name="resourceAssembly">
    /// The assembly that owns the embedded i18n resources to extract. Must be passed
    /// explicitly because <c>Assembly.GetExecutingAssembly()</c> here would return
    /// Anomaly.Client.Api, but the resources live in Anomaly.Client.
    /// </param>
    internal LocalizationService(string userDataDir, Assembly resourceAssembly)
        : this(userDataDir, resourceAssembly, null)
    {
    }

    /// <summary>
    /// Deterministic constructor used by focused runtime tests. Production callers
    /// use the two-argument overload and resolve the locale from the normal sources.
    /// </summary>
    internal LocalizationService(string userDataDir, Assembly resourceAssembly, string activeLocale)
    {
        _userDataDir = userDataDir;
        _resourceAssembly = resourceAssembly ?? throw new ArgumentNullException(nameof(resourceAssembly));
        _anomalyI18nDir = Path.Combine(userDataDir, "i18n", "anomaly");
        _localeOverride = string.IsNullOrWhiteSpace(activeLocale)
            ? GetOrCreateStringPreference("anomaly", "locale", string.Empty, "Locale override")
            : null;

        EnsureAnomalyExtracted();
        ActiveLocale = string.IsNullOrWhiteSpace(activeLocale) ? ResolveLocale() : activeLocale;
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

        LogWarning($"[Anomaly i18n] Missing key: {key}");
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
    /// Absolute path to a directory containing per-locale YAML files (en.yaml, fr.yaml, ...).
    /// All keys in those files must be prefixed with the mod's namespace.
    /// </param>
    /// <returns>True if registered; false if rejected (invalid namespace or duplicate).</returns>
    public bool RegisterMod(string modNamespace, string i18nDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(modNamespace))
        {
            LogError("[Anomaly i18n] RegisterMod: namespace must not be empty.");
            return false;
        }

        modNamespace = modNamespace.ToLowerInvariant();

        if (!NamespaceRegex.IsMatch(modNamespace))
        {
            LogError($"[Anomaly i18n] RegisterMod: invalid namespace '{modNamespace}'. " +
                     "Must be lowercase alphanumeric with dots (e.g., 'mymod').");
            return false;
        }

        if (modNamespace.StartsWith("anomaly.", StringComparison.Ordinal)
            || modNamespace == "anomaly"
            || modNamespace.StartsWith("launcher.", StringComparison.Ordinal)
            || modNamespace == "launcher")
        {
            LogError($"[Anomaly i18n] RegisterMod: namespace '{modNamespace}' is reserved.");
            return false;
        }

        if (_registeredNamespaces.Contains(modNamespace))
        {
            LogWarning($"[Anomaly i18n] RegisterMod: namespace '{modNamespace}' is already registered. Ignoring.");
            return false;
        }

        if (!Directory.Exists(i18nDirectoryPath))
        {
            LogError($"[Anomaly i18n] RegisterMod: directory '{i18nDirectoryPath}' does not exist.");
            return false;
        }

        try
        {
            // Stage the complete registration in private copies. Invalid YAML,
            // namespace escapes, or key collisions discard the copies without
            // publishing any entries or claiming the namespace.
            var stagedFallback = new Dictionary<string, string>(_fallback, StringComparer.Ordinal);
            var stagedActive = new Dictionary<string, string>(_active, StringComparer.Ordinal);
            var stagedNamespaces = new HashSet<string>(_registeredNamespaces, StringComparer.OrdinalIgnoreCase);
            var occupiedKeys = new HashSet<string>(_fallback.Keys, StringComparer.Ordinal);
            occupiedKeys.UnionWith(_active.Keys);

            if (!MergeModFileInto(
                    stagedFallback,
                    Path.Combine(i18nDirectoryPath, "en.yaml"),
                    modNamespace,
                    occupiedKeys,
                    required: true))
                return false;
            if (!MergeActiveModFromDir(stagedActive, i18nDirectoryPath, modNamespace, occupiedKeys))
                return false;
            if (!stagedNamespaces.Add(modNamespace))
                return false;

            _fallback = stagedFallback;
            _active = stagedActive;
            _registeredNamespaces = stagedNamespaces;
            LogMessage($"[Anomaly i18n] Registered mod namespace '{modNamespace}' from '{i18nDirectoryPath}'.");
            return true;
        }
        catch (Exception ex)
        {
            LogError($"[Anomaly i18n] RegisterMod '{modNamespace}' failed: {ex.Message}");
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
                if (!resourceName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) continue;

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
            LogWarning($"[Anomaly i18n] Extract failed: {ex.Message}");
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

        // 2. Anomaly MelonPreferences override
        if (!string.IsNullOrWhiteSpace(_localeOverride.Value))
            return _localeOverride.Value;

        // 3. System culture
        var sys = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
        if (!string.IsNullOrEmpty(sys))
            return sys;

        return "en";
    }

    // ==================== Translation loading ====================

    private void LoadAnomalyTranslations()
    {
        MergeFileInto(_fallback, Path.Combine(_anomalyI18nDir, "en.yaml"), "anomaly");

        var exact = LoadYamlFile(Path.Combine(_anomalyI18nDir, $"{ActiveLocale}.yaml"));
        if (exact != null)
        {
            foreach (var kvp in exact) AddTranslation(_active, kvp.Key, kvp.Value, _anomalyI18nDir);
            return;
        }

        var underscore = ActiveLocale.IndexOf('_');
        if (underscore > 0)
        {
            var prefix = ActiveLocale.Substring(0, underscore);
            var prefixMatch = LoadYamlFile(Path.Combine(_anomalyI18nDir, $"{prefix}.yaml"));
            if (prefixMatch != null)
            {
                foreach (var kvp in prefixMatch) AddTranslation(_active, kvp.Key, kvp.Value, _anomalyI18nDir);
                return;
            }
        }

        // No active match — mirror fallback so Get() still finds English.
        foreach (var kvp in _fallback) _active[kvp.Key] = kvp.Value;
    }

    private bool MergeActiveModFromDir(
        Dictionary<string, string> target,
        string dir,
        string expectedNamespace,
        ISet<string> occupiedKeys)
    {
        string exactPath = Path.Combine(dir, $"{ActiveLocale}.yaml");
        if (File.Exists(exactPath))
        {
            return MergeModFileInto(target, exactPath, expectedNamespace, occupiedKeys);
        }

        var underscore = ActiveLocale.IndexOf('_');
        if (underscore > 0)
        {
            var prefix = ActiveLocale.Substring(0, underscore);
            string prefixPath = Path.Combine(dir, $"{prefix}.yaml");
            if (File.Exists(prefixPath))
            {
                return MergeModFileInto(target, prefixPath, expectedNamespace, occupiedKeys);
            }
        }

        // Nothing translated for active locale — fallback file (en.yaml) was already merged into _fallback.
        return true;
    }

    private bool MergeModFileInto(
        Dictionary<string, string> target,
        string path,
        string expectedNamespace,
        ISet<string> occupiedKeys,
        bool required = false)
    {
        if (!TryLoadYamlFile(path, out Dictionary<string, string> loaded, out Exception error))
        {
            LogError(
                $"[Anomaly i18n] '{path}' is invalid YAML and registration was rejected: {error.Message}");
            return false;
        }

        if (loaded == null)
        {
            if (!required)
                return true;

            LogError($"[Anomaly i18n] Required English fallback '{path}' is missing.");
            return false;
        }

        return MergeTranslations(target, loaded, path, expectedNamespace, occupiedKeys);
    }

    private bool MergeFileInto(Dictionary<string, string> target, string path, string expectedNamespace)
    {
        var loaded = LoadYamlFile(path);
        if (loaded == null) return true;

        return MergeTranslations(target, loaded, path, expectedNamespace);
    }

    private bool MergeTranslations(
        Dictionary<string, string> target,
        Dictionary<string, string> loaded,
        string source,
        string expectedNamespace,
        ISet<string> occupiedKeys = null)
    {
        string prefix = expectedNamespace + ".";
        foreach (var kvp in loaded)
        {
            if (string.IsNullOrEmpty(kvp.Key) || !kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                LogError(
                    $"[Anomaly i18n] '{source}' contains key '{kvp.Key}' outside namespace '{expectedNamespace}'.");
                return false;
            }

            if (kvp.Value == null)
            {
                LogError(
                    $"[Anomaly i18n] '{source}' contains null value for key '{kvp.Key}'.");
                return false;
            }

            if (occupiedKeys != null && occupiedKeys.Contains(kvp.Key))
            {
                LogError(
                    $"[Anomaly i18n] Key '{kvp.Key}' from '{source}' collides with an existing translation.");
                return false;
            }

            AddTranslation(target, kvp.Key, kvp.Value, source);
        }

        return true;
    }

    private void AddTranslation(Dictionary<string, string> target, string key, string value, string source)
    {
        if (target.TryGetValue(key, out var existing) && existing != value)
        {
            LogWarning(
                $"[Anomaly i18n] Key '{key}' from '{source}' overwrites an existing translation.");
        }

        target[key] = value;
    }

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();

    private static bool TryLoadYamlFile(
        string path,
        out Dictionary<string, string> loaded,
        out Exception error)
    {
        loaded = null;
        error = null;

        if (!File.Exists(path))
            return true;

        try
        {
            var yaml = File.ReadAllText(path);
            loaded = YamlDeserializer.Deserialize<Dictionary<string, string>>(yaml)
                     ?? new Dictionary<string, string>(StringComparer.Ordinal);
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private static Dictionary<string, string> LoadYamlFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var yaml = File.ReadAllText(path);
            return YamlDeserializer.Deserialize<Dictionary<string, string>>(yaml);
        }
        catch
        {
            return null;
        }
    }

    private static MelonPreferences_Entry<string> GetOrCreateStringPreference(
        string categoryId,
        string entryId,
        string defaultValue,
        string displayName)
    {
        var category = MelonPreferences.CreateCategory(categoryId, "Anomaly");
        var entry = category.GetEntry<string>(entryId);
        return entry ?? category.CreateEntry(entryId, defaultValue, displayName);
    }

    private static void LogMessage(string message)
    {
        try { MelonLogger.Msg(message); }
        catch { /* Logging must never change localization state. */ }
    }

    private static void LogWarning(string message)
    {
        try { MelonLogger.Warning(message); }
        catch { /* Logging must never change localization state. */ }
    }

    private static void LogError(string message)
    {
        try { MelonLogger.Error(message); }
        catch { /* Logging must never change localization state. */ }
    }
}
