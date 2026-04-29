namespace Anomaly.Launcher.Models.Steam;

/// <summary>
/// One row in the version-picker. Fields are progressively enriched: the Steam API gives us
/// <see cref="ManifestId"/>, <see cref="ReleasedAt"/>, and sometimes <see cref="BuildId"/> up
/// front; <see cref="GameVersion"/> and <see cref="Status"/> are populated later as enrichment
/// sources come online.
/// </summary>
public sealed record SteamBuildCatalogEntry(
    ulong ManifestId,
    DateTimeOffset? ReleasedAt,
    string? BuildId,
    string? GameVersion,
    string? VersionLabel,
    string? Notes,
    SupportStatus Status)
{
    /// <summary>Convenience accessor for the old IsPatchCompatible check.</summary>
    public bool IsPatchCompatible => Status == SupportStatus.FullySupported;
}
