using Anomaly.Launcher.Models;
using Newtonsoft.Json.Linq;

namespace Anomaly.Launcher.Services;

public class VersionCheckService
{
    private const int ScpSlAppId = 700330;
    private readonly PathService _paths;

    public VersionCheckService(PathService paths)
    {
        _paths = paths;
    }

    public async Task<SteamBuildInfo> CheckBuildAsync()
    {
        // 1. Read local build info from appmanifest ACF
        string? localBuildId = null;
        string branch = "public";
        int stateFlags = 0;

        var acfPath = _paths.AppManifestPath;
        if (acfPath != null)
        {
            try
            {
                var data = AcfParser.ParseFile(acfPath);

                data.TryGetValue("AppState.buildid", out localBuildId);

                if (data.TryGetValue("AppState.UserConfig.betakey", out var betaKey)
                    && !string.IsNullOrEmpty(betaKey))
                {
                    branch = betaKey;
                }

                if (data.TryGetValue("AppState.StateFlags", out var flagsStr)
                    && int.TryParse(flagsStr, out var flags))
                {
                    stateFlags = flags;
                }
            }
            catch
            {
                // ACF unreadable — continue with null local data
            }
        }

        // 2. Fetch remote build info from steamcmd.net API
        string? onlineBuildId = null;
        string? errorMessage = null;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = $"https://api.steamcmd.net/v1/info/{ScpSlAppId}";
            var response = await client.GetStringAsync(url);
            var json = JObject.Parse(response);

            var branchData = json["data"]?[$"{ScpSlAppId}"]?["depots"]?["branches"]?[branch];
            onlineBuildId = branchData?["buildid"]?.Value<string>();
        }
        catch (Exception ex)
        {
            errorMessage = $"Unable to check online build: {ex.Message}";
        }

        return new SteamBuildInfo
        {
            LocalBuildId = localBuildId,
            OnlineBuildId = onlineBuildId,
            Branch = branch,
            StateFlags = stateFlags,
            ErrorMessage = errorMessage
        };
    }
}
