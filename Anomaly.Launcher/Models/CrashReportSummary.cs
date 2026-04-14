namespace Anomaly.Launcher.Models;

public class CrashReportSummary
{
    public string? ReportId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? TriggerType { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public string? StackTrace { get; set; }
    public string[]? RecentLogEntries { get; set; }
    public CrashSystemInfo? SystemInfo { get; set; }
    public CrashGameState? GameState { get; set; }
    public string[]? LoadedMods { get; set; }
    public DateTime LastSuccessfulFrame { get; set; }
    public long FrameCount { get; set; }
    public string? MiniDumpPath { get; set; }
}

public class CrashSystemInfo
{
    public string? OperatingSystem { get; set; }
    public int ProcessorCount { get; set; }
    public string? CpuType { get; set; }
    public int SystemMemoryMb { get; set; }
    public string? GpuName { get; set; }
    public string? UnityVersion { get; set; }
    public string? MelonLoaderVersion { get; set; }
    public long ManagedMemoryBytes { get; set; }
}

public class CrashGameState
{
    public string? CurrentScene { get; set; }
    public bool IsConnected { get; set; }
    public string? ServerIp { get; set; }
    public int PlayerCount { get; set; }
    public bool IsClientReady { get; set; }
}
