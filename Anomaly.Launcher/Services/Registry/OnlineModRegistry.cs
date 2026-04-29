using Anomaly.Launcher.Services.Platform;

namespace Anomaly.Launcher.Services.Registry;

/// <summary>
/// <see cref="IModRegistry"/> backed by the live Anomaly mods platform. Returns mods filtered
/// by an optional game-version. Failures (offline, server down) surface as an empty list — the
/// view-model layer is expected to fall back to its disk cache.
/// </summary>
public sealed class OnlineModRegistry : IModRegistry
{
    private readonly PlatformApiClient _client;
    private readonly Func<string?> _gameVersionAccessor;

    public OnlineModRegistry(PlatformApiClient client, Func<string?> gameVersionAccessor)
    {
        _client = client;
        _gameVersionAccessor = gameVersionAccessor;
    }

    public string Name => $"Online ({_client.Options.BaseUrl})";
    public bool IsOnline => true;

    public async Task<IReadOnlyList<ModRegistryEntry>> ListAsync(CancellationToken ct = default)
    {
        var gv = _gameVersionAccessor();
        var response = await _client.ListModsAsync(gameVersion: gv, ct: ct);
        if (response == null) return Array.Empty<ModRegistryEntry>();

        return response.Items
            .Select(m => new ModRegistryEntry(
                Id: m.Id,
                DisplayName: m.Name,
                Version: m.LatestVersion?.Semver,
                TargetGameVersion: m.LatestVersion?.GameVersions.FirstOrDefault(),
                DownloadUrl: null,
                IsInstalled: false))
            .ToList();
    }
}
