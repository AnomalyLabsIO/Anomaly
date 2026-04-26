using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;

namespace Anomaly.Launcher.Services.Localization;

/// <summary>
/// Loads JSON translation files, resolves the active locale using the priority chain
/// (SCPSL registry -> config.json -> CultureInfo -> en), and looks up translated strings
/// with fallback to English.
/// </summary>
internal class LocalizationService
{
    private readonly string _i18nDir;
    private readonly string _configPath;
    private Dictionary<string, string> _active = new();
    private Dictionary<string, string> _fallback = new();

    public string ActiveLocale { get; private set; } = "en";

    public LocalizationService(string anomalyDataDir)
    {
        _i18nDir = Path.Combine(anomalyDataDir, "i18n");
        _configPath = Path.Combine(anomalyDataDir, "config.json");

        EnsureExtracted(anomalyDataDir);
        ActiveLocale = ResolveLocale();
        LoadTranslations();

        // Authoritative English fallback lives inside the assembly, not on disk. Ensures new
        // keys added to Resources/i18n/en.json are picked up on the very next run, regardless
        // of whether the extracted copy at %APPDATA%/Anomaly/i18n/en.json has been refreshed.
        var embeddedEn = LoadEmbedded("en");
        if (embeddedEn != null)
            _fallback = embeddedEn;
    }

    public string Get(string key)
    {
        if (_active.TryGetValue(key, out var value))
            return value;
        if (_fallback.TryGetValue(key, out var fallback))
            return fallback;

        System.Diagnostics.Debug.WriteLine($"[i18n] Missing key: {key}");
        return key;
    }

    public string Get(string key, params object?[] args)
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

    // ==================== Extraction (initial launcher run + upgrades) ====================

    private void EnsureExtracted(string anomalyDataDir)
    {
        try
        {
            if (!Directory.Exists(anomalyDataDir))
                Directory.CreateDirectory(anomalyDataDir);
            if (!Directory.Exists(_i18nDir))
                Directory.CreateDirectory(_i18nDir);

            var assembly = Assembly.GetExecutingAssembly();
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                // Looking for "Anomaly.Launcher.Resources.i18n.<name>.json"
                if (!resourceName.Contains(".Resources.i18n.", StringComparison.Ordinal)) continue;
                if (!resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                var fileName = ExtractFileName(resourceName);
                var destPath = Path.Combine(_i18nDir, fileName);

                // Always overwrite bundled files — the embedded resource is authoritative, and
                // stale extracted copies would hide newly-added keys. Users who want to
                // customize translations should add their own locale file (e.g., pt_BR.json)
                // rather than editing the bundled ones.
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var file = File.Create(destPath);
                stream.CopyTo(file);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[i18n] Extract failed: {ex.Message}");
        }
    }

    private static string ExtractFileName(string resourceName)
    {
        // Resource names look like "Anomaly.Launcher.Resources.i18n.en.json"
        // We want "en.json"
        const string marker = ".Resources.i18n.";
        var idx = resourceName.IndexOf(marker, StringComparison.Ordinal);
        return idx >= 0 ? resourceName[(idx + marker.Length)..] : resourceName;
    }

    private static string GetAssemblyVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
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
                var config = JsonConvert.DeserializeObject<LauncherConfig>(json);
                if (!string.IsNullOrEmpty(config?.Locale))
                    return config.Locale;
            }
            catch
            {
                // Ignore malformed config
            }
        }

        // 3. System culture
        var systemLocale = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
        if (!string.IsNullOrEmpty(systemLocale))
            return systemLocale;

        // 4. Final fallback
        return "en";
    }

    // ==================== Translation loading ====================

    private void LoadTranslations()
    {
        _fallback = LoadFile("en") ?? new Dictionary<string, string>();

        // Try exact match first (e.g., pt_BR.json)
        var exact = LoadFile(ActiveLocale);
        if (exact != null)
        {
            _active = exact;
            return;
        }

        // Fall back to language-only prefix (e.g., pt_BR -> pt)
        var underscore = ActiveLocale.IndexOf('_');
        if (underscore > 0)
        {
            var prefix = ActiveLocale[..underscore];
            var prefixMatch = LoadFile(prefix);
            if (prefixMatch != null)
            {
                _active = prefixMatch;
                return;
            }
        }

        // No match — use fallback for everything
        _active = _fallback;
    }

    private Dictionary<string, string>? LoadFile(string locale)
    {
        try
        {
            var path = Path.Combine(_i18nDir, $"{locale}.json");
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

    private static Dictionary<string, string>? LoadEmbedded(string locale)
    {
        try
        {
            var resourceName = $"Anomaly.Launcher.Resources.i18n.{locale}.json";
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return null;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private class LauncherConfig
    {
        [JsonProperty("locale")]
        public string? Locale { get; set; }
    }
}
