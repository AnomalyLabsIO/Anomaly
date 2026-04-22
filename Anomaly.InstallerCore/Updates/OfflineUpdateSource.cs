namespace Anomaly.InstallerCore.Updates;

/// <summary>
/// No-op update source. Mirror of Anomaly.Launcher.Services.Updates.OfflineUpdateSource.
/// </summary>
public sealed class OfflineUpdateSource : IUpdateSource
{
    public string Name => "Offline";

    public Task<UpdateInfo?> CheckForUpdateAsync(string componentId, string currentVersion, CancellationToken ct = default)
        => Task.FromResult<UpdateInfo?>(null);
}
