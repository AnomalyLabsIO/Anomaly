namespace Anomaly.Launcher.Services.Registry;

/// <summary>
/// A source of available mods — local filesystem, a GitHub release feed, a private
/// index, etc. Kept abstract so the online-mod-catalog feature (planned post-v1.0)
/// can drop in without touching view-models.
/// </summary>
public interface IModRegistry
{
    string Name { get; }
    bool IsOnline { get; }

    Task<IReadOnlyList<ModRegistryEntry>> ListAsync(CancellationToken ct = default);
}

/// <summary>
/// A single mod as reported by an <see cref="IModRegistry"/>. <see cref="DownloadUrl"/>
/// is null for local-only entries; it's the URL the Launcher will hit to fetch the
/// mod binary once online-install support lands.
/// </summary>
public sealed record ModRegistryEntry(
    string Id,
    string DisplayName,
    string? Version,
    string? TargetGameVersion,
    string? DownloadUrl,
    bool IsInstalled);
