#nullable disable

using System.Globalization;
using System.Reflection;
using System.Text.Json;
#if ANOMALY_CLIENT
using Anomaly.Client.Api.Internal.Localization;
#endif
using MelonLoader;
using MelonLoader.Utils;

// The Client copy must work even when the Bootstrap plugin is absent.
#if ANOMALY_CLIENT
namespace Anomaly.Client.Internal.Bootstrap;
#else
namespace Anomaly.Bootstrap;
#endif

internal static class BootstrapLocalization
{
    private static readonly Lazy<BootstrapFailureTranslations> Translations = new(() =>
        new BootstrapFailureTranslations(typeof(BootstrapLocalization).Assembly, ResolveLocale(), UserDataDirectory()));

    internal static string Get(string key, params object[] args) => Translations.Value.Get(key, args);

    private static string ResolveLocale()
    {
        string registryLocale = RegistryTxtReader.ReadLocale();
        if (!string.IsNullOrWhiteSpace(registryLocale)) return registryLocale;
        try
        {
            var category = MelonPreferences.CreateCategory("anomaly", "Anomaly");
            string configured = (category.GetEntry<string>("locale")
                ?? category.CreateEntry("locale", string.Empty)).Value;
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }
        catch { /* The failure dialog also works before preferences are available. */ }
        return CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
    }

    private static string UserDataDirectory()
    {
        try { return MelonEnvironment.UserDataDirectory; }
        catch { return null; }
    }
}

// Read only the flat, double-quoted failure entries from the base YAML format.
// JSON string decoding handles its quotes, Unicode and newline escapes without
// resolving Client.Api, game assemblies, or a YAML dependency during a failure.
internal sealed class BootstrapFailureTranslations
{
    internal const string KeyPrefix = "anomaly.bootstrap.failure.";
    private readonly Dictionary<string, string> _english;
    private readonly Dictionary<string, string> _active;

    internal BootstrapFailureTranslations(Assembly resources, string locale, string userDataDirectory = null)
    {
        _english = ReadEmbedded(resources, "en");
        _active = new(StringComparer.Ordinal);
        // Locale names come from the game registry/preferences, never from paths.
        if (string.IsNullOrWhiteSpace(locale) || locale.Any(character =>
                !(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')))
            locale = "en";
        int separator = locale.IndexOfAny(new[] { '_', '-' });
        if (separator > 0) MergeLocale(resources, locale.Substring(0, separator), userDataDirectory);
        MergeLocale(resources, locale, userDataDirectory);
    }

    internal string Get(string key, params object[] args)
    {
        string fallback = _english.TryGetValue(key, out string english) ? english : key;
        string template = _active.TryGetValue(key, out string active) ? active : fallback;
        try { return string.Format(CultureInfo.CurrentCulture, template, args); }
        catch (FormatException)
        {
            try { return string.Format(CultureInfo.CurrentCulture, fallback, args); }
            catch (FormatException) { return fallback; }
        }
    }

    private void MergeLocale(Assembly resources, string locale, string userDataDirectory)
    {
        foreach (var entry in ReadEmbedded(resources, locale)) _active[entry.Key] = entry.Value;
        if (string.IsNullOrEmpty(userDataDirectory)) return;
        try
        {
            string path = Path.Combine(userDataDirectory, "i18n", "anomaly", locale + ".yaml");
            if (!File.Exists(path)) return;
            using var reader = new StreamReader(path);
            foreach (var entry in ReadCatalog(reader)) _active[entry.Key] = entry.Value;
        }
        catch { /* Missing/unreadable translations must not hide the warning. */ }
    }

    private static Dictionary<string, string> ReadEmbedded(Assembly resources, string locale)
    {
        string suffix = ".Resources.i18n." + locale + ".yaml";
        string name = resources.GetManifestResourceNames().FirstOrDefault(resource => resource.EndsWith(suffix, StringComparison.Ordinal));
        if (name == null) return new(StringComparer.Ordinal);
        using var stream = resources.GetManifestResourceStream(name);
        if (stream == null) return new(StringComparer.Ordinal);
        using var reader = new StreamReader(stream);
        return ReadCatalog(reader);
    }

    internal static Dictionary<string, string> ReadCatalog(TextReader reader)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (!line.StartsWith("\"" + KeyPrefix, StringComparison.Ordinal)) continue;
            int separator = line.IndexOf("\":", StringComparison.Ordinal);
            if (separator < 0) continue;
            try
            {
                string key = JsonSerializer.Deserialize<string>(line.Substring(0, separator + 1));
                string value = JsonSerializer.Deserialize<string>(line.Substring(separator + 2).Trim());
                if (key != null && !string.IsNullOrWhiteSpace(value)) entries[key] = value;
            }
            catch (JsonException) { /* Ignore one broken entry and keep the English fallback. */ }
        }
        return entries;
    }
}
