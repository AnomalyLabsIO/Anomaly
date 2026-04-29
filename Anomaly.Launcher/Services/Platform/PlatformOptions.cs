namespace Anomaly.Launcher.Services.Platform;

/// <summary>
/// Where the launcher should talk to for online catalog + game-builds + blob downloads.
/// Read at startup from <c>%APPDATA%/Anomaly/platform.json</c> if present, otherwise the
/// production default. Override is useful for local dev (point at <c>http://localhost:4321</c>).
/// </summary>
public sealed class PlatformOptions
{
    public const string DefaultBaseUrl = "https://mods.anomalylabs.io";
    public const string DefaultUserAgent = "anomaly-launcher";

    public string BaseUrl { get; init; } = DefaultBaseUrl;
    public string UserAgent { get; init; } = DefaultUserAgent;

    public string ResolveAbsolute(string relative)
    {
        if (relative.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return relative;
        var baseUrl = BaseUrl.TrimEnd('/');
        var rel = relative.StartsWith('/') ? relative : "/" + relative;
        return baseUrl + rel;
    }

    public static PlatformOptions Load(PathService paths)
    {
        try
        {
            var path = Path.Combine(paths.AnomalyDataDir, "platform.json");
            if (!File.Exists(path)) return new PlatformOptions();
            var json = File.ReadAllText(path);
            var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<PlatformOptions>(json);
            return parsed ?? new PlatformOptions();
        }
        catch
        {
            return new PlatformOptions();
        }
    }
}
