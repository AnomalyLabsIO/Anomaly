namespace Anomaly.Launcher.Models.Steam;

public abstract record SteamLoginResult
{
    public sealed record Success(string AccountName, ulong SteamId) : SteamLoginResult;
    public sealed record QrExpired : SteamLoginResult;
    public sealed record UserDeclined : SteamLoginResult;
    public sealed record NetworkFailure(string Message) : SteamLoginResult;
    public sealed record TokenExpired : SteamLoginResult;
    public sealed record Cancelled : SteamLoginResult;
    public sealed record UnexpectedError(string Message) : SteamLoginResult;
}
