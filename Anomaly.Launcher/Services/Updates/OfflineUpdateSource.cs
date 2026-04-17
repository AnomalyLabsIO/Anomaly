namespace Anomaly.Launcher.Services.Updates;

/// <summary>
/// No-op update source that always reports "no update available". Wire this in by default
/// so orchestration code doesn't need null checks. Replaced with <see cref="GitHubReleaseSource"/>
/// (or a combined multi-source) once online updates activate.
/// </summary>
public sealed class OfflineUpdateSource : IUpdateSource
{
    public string Name => "Offline";

    public Task<UpdateInfo?> CheckForUpdateAsync(string componentId, string currentVersion, CancellationToken ct = default)
        => Task.FromResult<UpdateInfo?>(null);
}
