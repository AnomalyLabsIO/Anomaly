using Anomaly.InstallerCore;
using Anomaly.Launcher.Models;
using Newtonsoft.Json.Linq;

namespace Anomaly.Launcher.Services;

public class VersionCheckService
{
    private const int ScpSlAppId = 700330;
    private const string DefaultBranch = "public";

    private readonly PathService _paths;

    public VersionCheckService(PathService paths)
    {
        _paths = paths;
    }

    public async Task<SteamBuildInfo> CheckBuildAsync()
    {
        // Local build comes from the install-state sidecar written by InstallerService at the
        // end of a successful install. We no longer scrape Steam's appmanifest_700330.acf — the
        // launcher doesn't track a user-side Steam library anymore.
        var state = InstallStateSidecar.TryRead(_paths.LauncherDir);
        string? localBuildId = state?.BuildId;
        string branch = DefaultBranch;

        // Online build comes from the public steamcmd.net mirror.
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
            StateFlags = 0,
            ErrorMessage = errorMessage,
        };
    }
}
