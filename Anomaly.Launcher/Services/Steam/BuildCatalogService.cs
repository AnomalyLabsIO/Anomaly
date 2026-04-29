using System.Globalization;
using System.Reflection;
using Anomaly.Launcher.Models.Steam;
using Anomaly.Launcher.Services.Platform;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Anomaly.Launcher.Services.Steam;

/// <summary>
/// Provides the merged list of SCP:SL builds shown in the version picker.
///
/// Sources (deduped by <see cref="SteamBuildCatalogEntry.ManifestId"/>):
///  1. Embedded <c>builds_catalog.json</c> — historical map from SteamDB, converted from
///     ManifestAnalyzer's <c>steamdb_patch_manifest_map.csv</c>. Carries date + manifestId
///     + buildId + gameVersion (null if the analyzer couldn't resolve one).
///  2. Live <see cref="SteamDownloadService.FetchManifestListAsync"/> — authenticated PICS call
///     returning the current manifest per branch with release date + buildid.
///
/// <see cref="Merge"/> returns a filtered view depending on <c>includeAll</c>: the default main
/// list shows exactly one row per known gameVersion (the newest by date). <c>includeAll=true</c>
/// reveals every known manifest, including ones with no resolved gameVersion yet.
/// </summary>
public sealed class BuildCatalogService
{
    private const string ResourceName = "Anomaly.Launcher.Resources.Catalog.builds_catalog.json";

    public IReadOnlyList<SteamBuildCatalogEntry> LoadEmbeddedSeeds() => LoadEmbedded();

    /// <summary>
    /// Fetches the live game-builds list from the platform API. On success, mirrors the response
    /// into <paramref name="cacheFile"/> for offline use. On failure, falls back to whatever's
    /// in the cache (or an empty list if nothing's been seen yet). The caller still needs to
    /// pass the result through <see cref="Merge"/> to combine with embedded seeds.
    /// </summary>
    public async Task<IReadOnlyList<SteamBuildCatalogEntry>> FetchLiveAsync(
        PlatformApiClient client,
        string cacheFile,
        CancellationToken ct = default)
    {
        try
        {
            var response = await client.GetGameBuildsAsync(ct);
            if (response != null)
            {
                var entries = MapApiEntries(response.Entries);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                    File.WriteAllText(cacheFile, JsonConvert.SerializeObject(response, Formatting.Indented));
                }
                catch
                {
                    // Cache write failures are non-fatal — we still have the live list this session.
                }
                return entries;
            }
        }
        catch
        {
            // Network/parse failure — fall through to the cache.
        }

        try
        {
            if (File.Exists(cacheFile))
            {
                var cached = JsonConvert.DeserializeObject<GameBuildsResponse>(File.ReadAllText(cacheFile));
                if (cached?.Entries != null)
                    return MapApiEntries(cached.Entries);
            }
        }
        catch
        {
            // Corrupt cache — ignore.
        }

        return Array.Empty<SteamBuildCatalogEntry>();
    }

    private static IReadOnlyList<SteamBuildCatalogEntry> MapApiEntries(IEnumerable<GameBuildEntryDto> dtos)
    {
        var entries = new List<SteamBuildCatalogEntry>();
        foreach (var dto in dtos)
        {
            if (!ulong.TryParse(dto.ManifestId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var manifestId)
                || manifestId == 0) continue;
            DateTimeOffset? releasedAt = DateTimeOffset.TryParse(
                dto.Date, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt) ? dt : null;
            var gameVersion = string.IsNullOrWhiteSpace(dto.GameVersion) ? null : dto.GameVersion;
            entries.Add(new SteamBuildCatalogEntry(
                ManifestId: manifestId,
                ReleasedAt: releasedAt,
                BuildId: string.IsNullOrEmpty(dto.BuildId) ? null : dto.BuildId,
                GameVersion: gameVersion,
                VersionLabel: gameVersion,
                Notes: dto.Notes,
                Status: SupportStatus.Unknown));
        }
        return entries;
    }

    public IReadOnlyList<SteamBuildCatalogEntry> Merge(
        IReadOnlyList<SteamBuildCatalogEntry> live,
        bool includeAll)
    {
        var byManifest = new Dictionary<ulong, SteamBuildCatalogEntry>();
        foreach (var entry in live)
            byManifest[entry.ManifestId] = entry;

        foreach (var seed in LoadEmbedded())
        {
            if (byManifest.TryGetValue(seed.ManifestId, out var existing))
            {
                byManifest[seed.ManifestId] = seed with
                {
                    ReleasedAt = seed.ReleasedAt ?? existing.ReleasedAt,
                    BuildId = seed.BuildId ?? existing.BuildId,
                    GameVersion = seed.GameVersion ?? existing.GameVersion,
                };
            }
            else
            {
                byManifest[seed.ManifestId] = seed;
            }
        }

        IEnumerable<SteamBuildCatalogEntry> result = byManifest.Values;

        if (!includeAll)
        {
            // Main view: one row per gameVersion, keeping the newest. Drop entries without a
            // resolved gameVersion — those only appear in the show-all view.
            result = result
                .Where(e => !string.IsNullOrWhiteSpace(e.GameVersion))
                .GroupBy(e => e.GameVersion, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(e => e.ReleasedAt ?? DateTimeOffset.MinValue).First());
        }

        return result
            .OrderByDescending(e => e.ReleasedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(e => e.ManifestId)
            .ToList();
    }

    private static IReadOnlyList<SteamBuildCatalogEntry> LoadEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(ResourceName);
        if (stream == null)
            return Array.Empty<SteamBuildCatalogEntry>();

        using var reader = new StreamReader(stream);
        JObject root;
        try { root = JObject.Parse(reader.ReadToEnd()); }
        catch { return Array.Empty<SteamBuildCatalogEntry>(); }

        // New schema (v2): { "schemaVersion": 2, "entries": [...] }
        if (root["entries"] is JArray entriesArray)
            return ParseV2(entriesArray);

        // Legacy schema (v1): { "schemaVersion": 1, "builds": [...] }
        if (root["builds"] is JArray buildsArray)
            return ParseV1(buildsArray);

        return Array.Empty<SteamBuildCatalogEntry>();
    }

    private static IReadOnlyList<SteamBuildCatalogEntry> ParseV2(JArray items)
    {
        var entries = new List<SteamBuildCatalogEntry>(items.Count);
        foreach (var item in items)
        {
            var manifestIdStr = (string?)item["manifestId"];
            if (!ulong.TryParse(manifestIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var manifestId) || manifestId == 0)
                continue;

            var dateStr = (string?)item["date"];
            DateTimeOffset? releasedAt = DateTimeOffset.TryParse(
                dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt)
                ? dt : null;

            var buildId = (string?)item["buildId"];
            var gameVersion = (string?)item["gameVersion"];
            if (string.IsNullOrWhiteSpace(gameVersion)) gameVersion = null;

            entries.Add(new SteamBuildCatalogEntry(
                ManifestId: manifestId,
                ReleasedAt: releasedAt,
                BuildId: string.IsNullOrEmpty(buildId) ? null : buildId,
                GameVersion: gameVersion,
                VersionLabel: gameVersion,
                Notes: null,
                Status: SupportStatus.Unknown));
        }
        return entries;
    }

    private static IReadOnlyList<SteamBuildCatalogEntry> ParseV1(JArray items)
    {
        var entries = new List<SteamBuildCatalogEntry>(items.Count);
        foreach (var item in items)
        {
            var manifestIdStr = (string?)item["manifestId"];
            if (!ulong.TryParse(manifestIdStr, out var manifestId) || manifestId == 0)
                continue;

            var buildId = (string?)item["buildId"];
            if (buildId == "REPLACE_ME") continue;

            var versionLabel = (string?)item["versionLabel"];
            var notes = (string?)item["notes"];
            var gameVersion = (string?)item["gameVersion"];
            var releasedAtStr = (string?)item["releasedAt"];
            DateTimeOffset? releasedAt = DateTimeOffset.TryParse(releasedAtStr, out var dt) ? dt : null;

            var status = Enum.TryParse<SupportStatus>((string?)item["status"], ignoreCase: true, out var s)
                ? s
                : (item["isPatchCompatible"]?.Value<bool>() == true ? SupportStatus.FullySupported : SupportStatus.Unknown);

            entries.Add(new SteamBuildCatalogEntry(
                ManifestId: manifestId,
                ReleasedAt: releasedAt,
                BuildId: buildId,
                GameVersion: gameVersion,
                VersionLabel: versionLabel,
                Notes: notes,
                Status: status));
        }
        return entries;
    }
}
