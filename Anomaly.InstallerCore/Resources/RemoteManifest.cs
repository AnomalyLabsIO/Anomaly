using Newtonsoft.Json;

namespace Anomaly.InstallerCore.Resources;

/// <summary>
/// Parsed view of <c>anomaly-resources/manifest.json</c>. Carries the pinned SHA-256 (and
/// size, for sanity checks) for every file the installer fetches from the AnomalyLabs CDN.
/// Loaded once per install; passed to every resolver so they share the same view of truth.
/// </summary>
public sealed class RemoteManifest
{
    public const string ResourceRelativePath = "manifest.json";
    private const string EmbeddedResourceName = "manifest.json";
    private const int LiveFetchTimeoutSeconds = 5;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonProperty("resources")]
    public Dictionary<string, ResourceEntry> Resources { get; set; } = new();

    public sealed class ResourceEntry
    {
        [JsonProperty("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonProperty("size")]
        public long Size { get; set; }
    }

    /// <summary>
    /// Pinned SHA-256 for <paramref name="relativePath"/>, or <c>null</c> if the manifest
    /// doesn't list it (caller should warn and proceed without integrity check).
    /// </summary>
    public string? GetSha256(string relativePath)
    {
        return Resources.TryGetValue(relativePath, out var entry) && !string.IsNullOrWhiteSpace(entry.Sha256)
            ? entry.Sha256
            : null;
    }

    /// <summary>
    /// Loads the manifest. Tries the live copy from anomaly-resources first (5 s timeout),
    /// then falls back to the embedded copy shipped with InstallerCore. The embedded copy
    /// pins what was current when this launcher was built.
    /// </summary>
    public static async Task<RemoteManifest> LoadAsync(AnomalyResourcesClient client, CancellationToken ct = default)
    {
        try
        {
            string text = await client.FetchTextAsync(
                ResourceRelativePath,
                expectedSha256: null, // chicken-and-egg: the manifest can't pin itself
                timeout: TimeSpan.FromSeconds(LiveFetchTimeoutSeconds),
                ct).ConfigureAwait(false);

            var live = JsonConvert.DeserializeObject<RemoteManifest>(text);
            if (live != null && live.Resources.Count > 0)
                return live;

            ConsoleUI.PrintWarning("Live manifest fetch returned an empty payload. Falling back to embedded.");
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintWarning(
                $"Could not fetch live anomaly-resources manifest ({ex.Message}). " +
                "Using embedded manifest — patches and binaries shipped with this launcher will be used.");
        }

        return LoadEmbedded();
    }

    /// <summary>
    /// Synchronous embedded-only load. Useful in code paths where async isn't worth the
    /// complexity (e.g. dry-run paths) or when the live manifest fetch already failed.
    /// </summary>
    public static RemoteManifest LoadEmbedded()
    {
        string json = EmbeddedResources.ReadText(EmbeddedResourceName);
        var manifest = JsonConvert.DeserializeObject<RemoteManifest>(json);
        if (manifest == null || manifest.Resources.Count == 0)
            throw new InstallerException("Embedded manifest.json is missing or has no resource entries.");
        return manifest;
    }
}
