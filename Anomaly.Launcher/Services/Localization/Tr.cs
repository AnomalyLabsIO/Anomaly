namespace Anomaly.Launcher.Services.Localization;

/// <summary>
/// Static facade for translation lookups. Must be registered once at app startup
/// via <see cref="Register"/> before any translation is requested.
/// </summary>
internal static class Tr
{
    private static LocalizationService? _service;

    public static void Register(LocalizationService service) => _service = service;

    public static string Get(string key)
    {
        return _service?.Get(key) ?? key;
    }

    public static string Get(string key, params object?[] args)
    {
        return _service?.Get(key, args) ?? key;
    }

    public static string ActiveLocale => _service?.ActiveLocale ?? "en";
}
