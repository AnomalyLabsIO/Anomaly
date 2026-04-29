using Newtonsoft.Json;

namespace Anomaly.Launcher.Services.Platform;

// Wire-format DTOs mirroring the platform's /api/v1/* responses. Kept narrow on purpose:
// the launcher consumes a small slice (browse + install + game-builds) and ignores the rest.

public sealed class CatalogResponse
{
    [JsonProperty("items")]
    public List<CatalogModDto> Items { get; set; } = new();

    [JsonProperty("total")]
    public int Total { get; set; }
}

public sealed class CatalogModDto
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("iconUrl")]
    public string? IconUrl { get; set; }

    [JsonProperty("author")]
    public CatalogAuthorDto? Author { get; set; }

    [JsonProperty("sourceUrl")]
    public string? SourceUrl { get; set; }

    [JsonProperty("downloads")]
    public int Downloads { get; set; }

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonProperty("createdAt")]
    public long CreatedAt { get; set; }

    [JsonProperty("updatedAt")]
    public long UpdatedAt { get; set; }

    [JsonProperty("latestVersion")]
    public CatalogVersionSummaryDto? LatestVersion { get; set; }
}

public sealed class CatalogAuthorDto
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("username")]
    public string Username { get; set; } = string.Empty;

    [JsonProperty("displayName")]
    public string? DisplayName { get; set; }

    [JsonProperty("avatarUrl")]
    public string? AvatarUrl { get; set; }
}

public class CatalogVersionSummaryDto
{
    [JsonProperty("semver")]
    public string Semver { get; set; } = string.Empty;

    [JsonProperty("gameVersions")]
    public List<string> GameVersions { get; set; } = new();

    [JsonProperty("publishedAt")]
    public long PublishedAt { get; set; }

    [JsonProperty("state")]
    public string State { get; set; } = "PENDING";

    [JsonProperty("hasServerPack")]
    public bool HasServerPack { get; set; }
}

public sealed class CatalogVersionDetailDto : CatalogVersionSummaryDto
{
    [JsonProperty("modId")]
    public string ModId { get; set; } = string.Empty;

    [JsonProperty("changelog")]
    public string? Changelog { get; set; }

    [JsonProperty("files")]
    public List<CatalogVersionFileDto> Files { get; set; } = new();

    [JsonProperty("dependencies")]
    public List<CatalogDependencyDto> Dependencies { get; set; } = new();
}

public sealed class CatalogVersionFileDto
{
    [JsonProperty("target")]
    public string Target { get; set; } = string.Empty;  // mod | userlib | userdata | labapi

    [JsonProperty("relPath")]
    public string RelPath { get; set; } = string.Empty;

    [JsonProperty("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonProperty("size")]
    public long Size { get; set; }

    [JsonProperty("scope")]
    public string Scope { get; set; } = "both";  // client | server | both

    [JsonProperty("url")]
    public string Url { get; set; } = string.Empty;
}

public sealed class CatalogDependencyDto
{
    [JsonProperty("modId")]
    public string ModId { get; set; } = string.Empty;

    [JsonProperty("versionReq")]
    public string VersionReq { get; set; } = string.Empty;
}

public sealed class GameBuildsResponse
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonProperty("entries")]
    public List<GameBuildEntryDto> Entries { get; set; } = new();
}

public sealed class GameBuildEntryDto
{
    [JsonProperty("date")]
    public string? Date { get; set; }

    [JsonProperty("manifestId")]
    public string? ManifestId { get; set; }

    [JsonProperty("buildId")]
    public string? BuildId { get; set; }

    [JsonProperty("gameVersion")]
    public string? GameVersion { get; set; }

    [JsonProperty("notes")]
    public string? Notes { get; set; }

    [JsonProperty("source")]
    public string? Source { get; set; }
}
