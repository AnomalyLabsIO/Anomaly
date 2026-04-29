namespace Anomaly.Launcher.Models.Steam;

public abstract record SteamDownloadResult
{
    public sealed record Success(string DestinationPath, string BuildId, ulong ManifestId) : SteamDownloadResult;
    public sealed record Cancelled : SteamDownloadResult;
    public sealed record NotOwned : SteamDownloadResult;
    public sealed record InsufficientDiskSpace(long RequiredBytes, long AvailableBytes) : SteamDownloadResult;
    public sealed record NetworkFailure(string Message) : SteamDownloadResult;
    public sealed record UnexpectedError(string Message) : SteamDownloadResult;
}
