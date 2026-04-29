namespace Anomaly.Launcher.Models.Steam;

public sealed record SteamDownloadProgress(
    long BytesDownloaded,
    long BytesTotal,
    string? CurrentFile,
    double BytesPerSecond);
