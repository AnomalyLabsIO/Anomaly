using System.Runtime.InteropServices;

namespace Anomaly.Launcher.Services.Platform;

/// <summary>
/// Owns the <c>anomaly-launcher://</c> URI scheme — registers the handler on Windows so the
/// "Install via Launcher" deep links from <see cref="PlatformOptions.BaseUrl"/> route to this
/// process, and parses URIs of the form <c>anomaly-launcher://install?mod=ID&amp;version=SEMVER</c>.
/// </summary>
public static class UrlProtocolService
{
    public const string Scheme = "anomaly-launcher";

    public sealed record InstallRequest(string ModId, string? Version);

    /// <summary>
    /// Best-effort: writes the per-user (HKCU) registry entries that map the
    /// <c>anomaly-launcher://</c> scheme to this exe. No admin required. Idempotent —
    /// rewrites the keys each launch so a re-installed exe path stays current.
    /// </summary>
    public static void RegisterOnStartup(string exePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            using var classes = Microsoft.Win32.Registry.CurrentUser
                .CreateSubKey(@"Software\Classes\" + Scheme, writable: true);
            classes.SetValue("", "URL:Anomaly Launcher Protocol");
            classes.SetValue("URL Protocol", "");

            using var iconKey = classes.CreateSubKey("DefaultIcon", writable: true);
            iconKey.SetValue("", $"\"{exePath}\",0");

            using var commandKey = classes.CreateSubKey(@"shell\open\command", writable: true);
            commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        catch
        {
            // Registration failure is non-fatal — the launcher still works without deep links.
        }
    }

    /// <summary>
    /// Picks an install request out of the process args. Returns null if the args don't contain
    /// an <c>anomaly-launcher://install?...</c> URI.
    /// </summary>
    public static InstallRequest? TryParseFromArgs(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (string.IsNullOrEmpty(arg)) continue;
            if (!arg.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(arg, UriKind.Absolute, out var uri)) continue;
            if (!string.Equals(uri.Host, "install", StringComparison.OrdinalIgnoreCase)) continue;

            var query = ParseQuery(uri.Query);
            if (!query.TryGetValue("mod", out var modId) || string.IsNullOrWhiteSpace(modId)) continue;
            query.TryGetValue("version", out var version);
            return new InstallRequest(modId, string.IsNullOrWhiteSpace(version) ? null : version);
        }
        return null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;
        var trimmed = query.StartsWith('?') ? query.Substring(1) : query;
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            var key = Uri.UnescapeDataString(pair.Substring(0, eq));
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1));
            result[key] = value;
        }
        return result;
    }
}
