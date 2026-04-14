namespace Anomaly.Launcher.Models;

public class SteamBuildInfo
{
    public string? LocalBuildId { get; init; }
    public string? OnlineBuildId { get; init; }
    public string Branch { get; init; } = "public";
    public int StateFlags { get; init; }
    public bool IsFullyInstalled => StateFlags == 4;
    public string? ErrorMessage { get; init; }
}
