namespace Anomaly.InstallerCore.Updates;

/// <summary>
/// Placeholder for the planned GitHub-release-based Installer self-update. Mirror of
/// Anomaly.Launcher.Services.Updates.GitHubReleaseSource. Not wired up yet — activating
/// it requires a self-swap bootstrap (the Installer can't overwrite its own running exe;
/// it needs to stage the new binary, exit, and have a helper rename).
///
/// When implementing:
///   1. Hit https://api.github.com/repos/{Repo}/releases/latest.
///   2. Compare tag_name to <paramref name="currentVersion"/>.
///   3. Return download URL for the Installer asset (Anomaly.Installer-{os}-{arch}.exe).
///   4. Caller stages the download to Installer.exe.new, relaunches, bootstrap swaps.
/// </summary>
public sealed class GitHubReleaseSource : IUpdateSource
{
    public string Repo { get; }
    public string Name => $"GitHub ({Repo})";

    public GitHubReleaseSource(string repo)
    {
        Repo = repo;
    }

    public Task<UpdateInfo?> CheckForUpdateAsync(string componentId, string currentVersion, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "GitHubReleaseSource is scaffolded but not wired up. See class docstring for the implementation steps.");
    }
}
