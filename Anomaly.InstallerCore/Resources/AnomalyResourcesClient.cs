namespace Anomaly.InstallerCore.Resources;

/// <summary>
/// Single entry point for fetching files from the AnomalyLabs CDN
/// (<c>https://github.com/AnomalyLabsIO/anomaly-resources</c>). Mirrors the repo's directory
/// layout into <c>%APPDATA%/Anomaly/cache/&lt;relativePath&gt;</c> so repeat installs reuse the
/// downloaded copy without hitting the network. Every fetch is integrity-checked against
/// the SHA-256 pinned in <see cref="RemoteManifest"/>.
/// </summary>
public sealed class AnomalyResourcesClient
{
    private const string DefaultBaseUrl = "https://raw.githubusercontent.com/AnomalyLabsIO/anomaly-resources/main/";
    private const string BaseUrlEnvVar = "ANOMALY_RESOURCES_BASE_URL";
    private const int DefaultFetchTimeoutSeconds = 60;

    private readonly string _baseUrl;
    private readonly string _cacheRoot;

    public AnomalyResourcesClient()
    {
        _baseUrl = ResolveBaseUrl();

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _cacheRoot = Path.Combine(appData, "Anomaly", "cache");
    }

    /// <summary>The URL the client is currently configured to download from. Useful for log lines.</summary>
    public string BaseUrl => _baseUrl;

    /// <summary>
    /// Returns the absolute path to a verified local copy of <paramref name="relativePath"/>.
    /// Hits the cache first; on cache miss or hash mismatch, downloads from the CDN and verifies.
    /// </summary>
    public async Task<string> GetOrDownloadAsync(
        string relativePath,
        string? expectedSha256,
        TimeSpan? timeout = null,
        string? progressLabel = null,
        CancellationToken ct = default)
    {
        string cachePath = GetCachePath(relativePath);

        if (TryUseCache(cachePath, expectedSha256))
            return cachePath;

        string url = _baseUrl + relativePath;
        ConsoleUI.PrintInfo($"  Downloading {relativePath} from {url}");

        try
        {
            await HttpDownloader.DownloadFileAsync(
                url,
                cachePath,
                timeout ?? TimeSpan.FromSeconds(DefaultFetchTimeoutSeconds),
                progressLabel,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InstallerException(
                $"Failed to download {relativePath} from anomaly-resources: {ex.Message}. " +
                $"Check your connection, or download {url} manually into {cachePath}.");
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            string actual = ResourceIntegrity.ComputeFileSha256(cachePath);
            ResourceIntegrity.VerifyHash(relativePath, expectedSha256, actual);
        }

        return cachePath;
    }

    /// <summary>
    /// One-shot text fetch (e.g. for the manifest itself). Bypasses the on-disk cache so
    /// the manifest is always fresh; the cache is for the binaries it points at.
    /// </summary>
    public async Task<string> FetchTextAsync(
        string relativePath,
        string? expectedSha256,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        string url = _baseUrl + relativePath;
        using var http = new HttpClient { Timeout = timeout };
        string text = await http.GetStringAsync(url, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            string actual = ResourceIntegrity.ComputeSha256(System.Text.Encoding.UTF8.GetBytes(text));
            ResourceIntegrity.VerifyHash(relativePath, expectedSha256, actual);
        }

        return text;
    }

    /// <summary>Absolute path on disk where <paramref name="relativePath"/> is (or would be) cached.</summary>
    public string GetCachePath(string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_cacheRoot, normalized);
    }

    private static string ResolveBaseUrl()
    {
        string? overrideUrl = Environment.GetEnvironmentVariable(BaseUrlEnvVar);
        if (string.IsNullOrWhiteSpace(overrideUrl))
            return DefaultBaseUrl;

        // Trailing slash is required so simple concatenation produces correct URLs.
        return overrideUrl.EndsWith('/') ? overrideUrl : overrideUrl + "/";
    }

    private static bool TryUseCache(string cachePath, string? expectedSha256)
    {
        if (!File.Exists(cachePath))
            return false;

        if (string.IsNullOrWhiteSpace(expectedSha256))
            return true;  // No hash to verify against — trust the cache.

        try
        {
            string actual = ResourceIntegrity.ComputeFileSha256(cachePath);
            return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
