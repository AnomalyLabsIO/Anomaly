namespace Anomaly.Installer.Updates;

/// <summary>
/// Source of updates for the Installer itself. Mirror of
/// Anomaly.Launcher.Services.Updates.IUpdateSource — the Installer can't reference the
/// Launcher project, so the shape is duplicated. Keep the two contracts aligned by
/// hand; if you change one, mirror it in the other.
/// </summary>
public interface IUpdateSource
{
    string Name { get; }

    Task<UpdateInfo?> CheckForUpdateAsync(string componentId, string currentVersion, CancellationToken ct = default);
}

public sealed record UpdateInfo(
    string ComponentId,
    string CurrentVersion,
    string LatestVersion,
    string? DownloadUrl,
    string? ReleaseNotesUrl,
    DateTime? PublishedAtUtc);
