using Newtonsoft.Json;

namespace Anomaly.Launcher.Services.Platform;

/// <summary>
/// HTTP client for the Anomaly mods platform. Single shared <see cref="HttpClient"/> instance
/// is intentional — the launcher is long-lived and the catalog endpoints are GETs against a
/// single host. Disposable so we can clean up at shutdown.
/// </summary>
public sealed class PlatformApiClient : IDisposable
{
    private readonly HttpClient _http;
    public PlatformOptions Options { get; }

    public PlatformApiClient(PlatformOptions options, HttpClient? http = null)
    {
        Options = options;
        _http = http ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<CatalogResponse?> ListModsAsync(
        string? gameVersion = null,
        string? query = null,
        CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameVersion)) qs.Add($"gameVersion={Uri.EscapeDataString(gameVersion)}");
        if (!string.IsNullOrWhiteSpace(query)) qs.Add($"q={Uri.EscapeDataString(query)}");
        var path = "/api/v1/mods" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        return await GetJsonAsync<CatalogResponse>(path, ct);
    }

    public Task<CatalogModDto?> GetModAsync(string id, CancellationToken ct = default) =>
        GetJsonAsync<CatalogModDto>($"/api/v1/mods/{Uri.EscapeDataString(id)}", ct);

    public Task<CatalogVersionDetailDto?> GetVersionAsync(string id, string semver, CancellationToken ct = default) =>
        GetJsonAsync<CatalogVersionDetailDto>(
            $"/api/v1/mods/{Uri.EscapeDataString(id)}/versions/{Uri.EscapeDataString(semver)}", ct);

    public Task<GameBuildsResponse?> GetGameBuildsAsync(CancellationToken ct = default) =>
        GetJsonAsync<GameBuildsResponse>("/api/v1/game-builds", ct);

    /// <summary>
    /// Streams a blob into the provided destination stream while computing SHA-256.
    /// Returns the computed hash as lowercase hex.
    /// Throws if the response is not 2xx or the computed hash doesn't match <paramref name="expectedSha256"/>.
    /// </summary>
    public async Task<string> DownloadBlobAsync(
        string sha256,
        Stream destination,
        string expectedSha256,
        CancellationToken ct = default)
    {
        var url = Options.ResolveAbsolute($"/api/v1/blobs/{sha256}");
        using var res = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        await using var src = await res.Content.ReadAsStreamAsync(ct);

        using var hasher = System.Security.Cryptography.SHA256.Create();
        var buffer = new byte[81920];
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            hasher.TransformBlock(buffer, 0, read, null, 0);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var actual = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"sha256 mismatch for blob {sha256}: expected {expectedSha256}, got {actual}");
        return actual;
    }

    private async Task<T?> GetJsonAsync<T>(string relativePath, CancellationToken ct) where T : class
    {
        var url = Options.ResolveAbsolute(relativePath);
        using var res = await _http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode) return null;
        var body = await res.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<T>(body);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
