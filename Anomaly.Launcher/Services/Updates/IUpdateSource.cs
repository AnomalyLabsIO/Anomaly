namespace Anomaly.Launcher.Services.Updates;

/// <summary>
/// Source of updates for a deployable component (Launcher, Installer, Anomaly.Client mod).
/// Abstracted so the GitHub-release-based self-update feature (planned post-v1.0) can
/// drop in without touching the UI or update-orchestration code.
/// </summary>
public interface IUpdateSource
{
    string Name { get; }

    /// <summary>
    /// Returns the latest published version for the identified component, or null if
    /// this source has no opinion (offline source, missing network, disabled, etc.).
    /// </summary>
    Task<UpdateInfo?> CheckForUpdateAsync(string componentId, string currentVersion, CancellationToken ct = default);
}

/// <summary>
/// Metadata describing a single available update. The download is not performed by
/// the update source itself — the orchestration layer decides what to do (prompt,
/// download-then-swap, background-download, etc.).
/// </summary>
public sealed record UpdateInfo(
    string ComponentId,
    string CurrentVersion,
    string LatestVersion,
    string? DownloadUrl,
    string? ReleaseNotesUrl,
    DateTime? PublishedAtUtc);
