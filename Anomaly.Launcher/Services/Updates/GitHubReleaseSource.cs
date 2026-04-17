namespace Anomaly.Launcher.Services.Updates;

/// <summary>
/// Placeholder for the planned GitHub-release-based update source. Not wired into the
/// Launcher yet — activated once the release artifact pipeline and self-swap bootstrap
/// are ready.
///
/// When implementing:
///   1. Hit <c>https://api.github.com/repos/{Repo}/releases/latest</c> with an HttpClient.
///   2. Compare <c>tag_name</c> (stripped of any "v" prefix) to <paramref name="currentVersion"/>
///      using <see cref="System.Version"/>.
///   3. Pick the release asset whose name matches the component (e.g. "Anomaly.Launcher-win-x64.exe")
///      and return its browser_download_url as <see cref="UpdateInfo.DownloadUrl"/>.
///   4. Handle rate limits (unauthenticated GitHub API is 60/hr per IP — plenty for a desktop app).
///   5. Cache the last-known-good response so the Launcher doesn't always hit the network.
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
