using System;

namespace Anomaly.Client.API.Localization;

/// <summary>
/// Public translation facade. Third-party mods should call <see cref="RegisterMod"/>
/// to register their own namespace, then use <see cref="Get(string)"/> / <see cref="Get(string, object[])"/>
/// to look up keys.
///
/// See <c>LOCALIZATION.md</c> in this directory for the conventions and registration flow.
/// </summary>
public static class Tr
{
    private static LocalizationService _service;

    /// <summary>Internal — called by Core on init. Third-party mods should not call this.</summary>
    internal static void Register(LocalizationService service) => _service = service;

    /// <summary>
    /// Register a third-party mod's translation namespace.
    /// </summary>
    /// <param name="modNamespace">
    /// Lowercase dotted identifier (e.g., "mymod"). Reserved prefixes: "anomaly", "launcher".
    /// </param>
    /// <param name="i18nDirectoryPath">
    /// Directory containing per-locale JSON files (en.json, fr.json, ...). All keys must use the mod's namespace prefix.
    /// </param>
    /// <returns>True if registered; false on invalid namespace, duplicate, or missing directory.</returns>
    public static bool RegisterMod(string modNamespace, string i18nDirectoryPath)
    {
        if (_service == null) return false;
        return _service.RegisterMod(modNamespace, i18nDirectoryPath);
    }

    /// <summary>Look up a translation key. Returns the key itself if missing.</summary>
    public static string Get(string key)
    {
        return _service == null ? (key ?? string.Empty) : _service.Get(key);
    }

    /// <summary>Look up a translation key and format with positional arguments ({0}, {1}, ...).</summary>
    public static string Get(string key, params object[] args)
    {
        return _service == null ? (key ?? string.Empty) : _service.Get(key, args);
    }

    /// <summary>The currently active locale (e.g., "en", "fr", "pt_BR").</summary>
    public static string ActiveLocale => _service?.ActiveLocale ?? "en";
}
