using System.Text.Json;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Diagnostics;
using Anomaly.Client.Auth;
using MelonLoader;

namespace Anomaly.Client.Diagnostics;

public static class CrashReportWriter
{
    private const int MaxArtifactSets = 20;
    private const long MaxArtifactBytes = 256L * 1024L * 1024L;
    private static readonly TimeSpan MaxArtifactAge = TimeSpan.FromDays(30);
    private static readonly object FileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string Write(CrashReport report)
    {
        if (report == null)
            return null;

        try
        {
            lock (FileLock)
            {
                NormalizeIdentity(report);
                string dir = Paths.CrashReportsPath;
                Directory.CreateDirectory(dir);

                string filePath = GetArtifactPath(report, ".json");
                var sanitized = DiagnosticRedactor.CreateSanitizedCopy(report);
                string json = JsonSerializer.Serialize(sanitized, JsonOptions);
                File.WriteAllText(filePath, json);

                CleanupOldReportsCore(dir);
                MelonLogger.Warning(
                    $"Diagnostic artifact written locally: {filePath}. " +
                    DiagnosticPrivacyPolicy.ReviewBeforeShareNotice);
                return filePath;
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"CrashReportWriter: Failed to write report: {ex.Message}");
            return null;
        }
    }

    internal static string GetArtifactPath(CrashReport report, string extension)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (string.IsNullOrEmpty(extension) || extension[0] != '.')
            throw new ArgumentException("Artifact extension must begin with a period.", nameof(extension));

        NormalizeIdentity(report);
        string timestamp = report.Timestamp.ToString("yyyyMMdd-HHmmss");
        string trigger = SanitizeTrigger(report.TriggerType);
        string shortId = report.ReportId.Substring(0, 12);
        return Path.Combine(
            Paths.CrashReportsPath,
            $"crash-{timestamp}-{trigger}-{shortId}{extension.ToLowerInvariant()}");
    }

    public static void CleanupOldReports()
    {
        try
        {
            lock (FileLock)
            {
                string dir = Paths.CrashReportsPath;
                if (Directory.Exists(dir))
                    CleanupOldReportsCore(dir);
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"CrashReportWriter: Cleanup failed: {ex.Message}");
        }
    }

    private static void CleanupOldReportsCore(string dir)
    {
        var groups = Directory.GetFiles(dir, "crash-*.*", SearchOption.TopDirectoryOnly)
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ArtifactSet(group.Key, group.ToArray()))
            .OrderByDescending(group => group.LastWriteUtc)
            .ToArray();

        DateTime cutoff = DateTime.UtcNow - MaxArtifactAge;
        long retainedBytes = 0;
        int retainedSets = 0;
        int removedSets = 0;

        foreach (var group in groups)
        {
            bool keep = group.LastWriteUtc >= cutoff &&
                        retainedSets < MaxArtifactSets &&
                        group.TotalBytes <= MaxArtifactBytes - retainedBytes;
            if (keep)
            {
                retainedSets++;
                retainedBytes += group.TotalBytes;
                continue;
            }

            foreach (string file in group.Files)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Retention cleanup is best effort, especially during a crash.
                }
            }
            removedSets++;
        }

        if (removedSets > 0)
            MelonLogger.Msg($"CrashReportWriter: Removed {removedSets} expired or over-budget artifact set(s).");
    }

    private static void NormalizeIdentity(CrashReport report)
    {
        if (!Guid.TryParseExact(report.ReportId, "N", out _))
            report.ReportId = Guid.NewGuid().ToString("N");
        if (report.Timestamp == default)
            report.Timestamp = DateTime.UtcNow;
        report.TriggerType = SanitizeTrigger(report.TriggerType);
    }

    private static string SanitizeTrigger(string trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger))
            return "Unknown";

        var chars = trigger.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
            .Take(48)
            .ToArray();
        return chars.Length == 0 ? "Unknown" : new string(chars);
    }

    private sealed class ArtifactSet
    {
        public string Name { get; }
        public string[] Files { get; }
        public DateTime LastWriteUtc { get; }
        public long TotalBytes { get; }

        public ArtifactSet(string name, string[] files)
        {
            Name = name;
            Files = files;
            DateTime lastWrite = DateTime.MinValue;
            long totalBytes = 0;
            foreach (string file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc > lastWrite)
                        lastWrite = info.LastWriteTimeUtc;
                    if (info.Length <= long.MaxValue - totalBytes)
                        totalBytes += info.Length;
                    else
                        totalBytes = long.MaxValue;
                }
                catch
                {
                    totalBytes = long.MaxValue;
                }
            }
            LastWriteUtc = lastWrite;
            TotalBytes = totalBytes;
        }
    }
}

internal static class DiagnosticPrivacyPolicy
{
    private const string MiniDumpOptInVariable = "ANOMALY_ENABLE_MINIDUMPS";

    internal static bool MiniDumpsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(MiniDumpOptInVariable),
            "1",
            StringComparison.Ordinal);

    internal static string ReviewBeforeShareNotice =>
        "Review every artifact before sharing; reports are redacted on a best-effort basis, " +
        "and opted-in minidumps can still contain private memory.";

    internal static string MiniDumpStatus => MiniDumpsEnabled
        ? $"Minidumps are explicitly enabled through {MiniDumpOptInVariable}=1. {ReviewBeforeShareNotice}"
        : $"Minidumps are disabled by default. Set {MiniDumpOptInVariable}=1 only for an intentional local capture.";
}

internal static class DiagnosticRedactor
{
    private const int MaxFieldChars = 32 * 1024;
    private const int MaxLogEntryChars = 8 * 1024;

    internal static CrashReport CreateSanitizedCopy(CrashReport source)
    {
        var system = source.SystemInfo;
        var game = source.GameState;
        return new CrashReport
        {
            ReportId = source.ReportId,
            Timestamp = source.Timestamp,
            TriggerType = Redact(source.TriggerType),
            ExceptionType = Redact(source.ExceptionType),
            ExceptionMessage = Redact(source.ExceptionMessage),
            StackTrace = Redact(source.StackTrace),
            RecentLogEntries = RedactArray(source.RecentLogEntries, MaxLogEntryChars),
            SystemInfo = system == null ? null : new CrashSystemInfo
            {
                OperatingSystem = Redact(system.OperatingSystem),
                ProcessorCount = system.ProcessorCount,
                CpuType = Redact(system.CpuType),
                SystemMemoryMb = system.SystemMemoryMb,
                GpuName = Redact(system.GpuName),
                UnityVersion = Redact(system.UnityVersion),
                MelonLoaderVersion = Redact(system.MelonLoaderVersion),
                ManagedMemoryBytes = system.ManagedMemoryBytes
            },
            GameState = game == null ? null : new GameStateSnapshot
            {
                CurrentScene = Redact(game.CurrentScene),
                IsConnected = game.IsConnected,
                ServerIp = string.IsNullOrEmpty(game.ServerIp) ? null : "[redacted-server-endpoint]",
                PlayerCount = game.PlayerCount,
                IsClientReady = game.IsClientReady
            },
            LoadedMods = RedactArray(source.LoadedMods, MaxFieldChars),
            LastSuccessfulFrame = source.LastSuccessfulFrame,
            FrameCount = source.FrameCount,
            MiniDumpPath = string.IsNullOrEmpty(source.MiniDumpPath)
                ? null
                : Path.GetFileName(source.MiniDumpPath)
        };
    }

    private static string[] RedactArray(string[] values, int maxChars)
    {
        if (values == null)
            return null;
        var redacted = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
            redacted[i] = Redact(values[i], maxChars);
        return redacted;
    }

    private static string Redact(string value, int maxChars = MaxFieldChars)
    {
        string redacted = DiagnosticTextRedactor.Redact(value, maxChars);
        string nickname = AuthHandler.PlayerAuth.Username;
        if (string.IsNullOrWhiteSpace(redacted) || string.IsNullOrWhiteSpace(nickname))
            return redacted;

        string withoutNickname = redacted.Replace(
            nickname,
            "[redacted-user]",
            StringComparison.OrdinalIgnoreCase);
        return withoutNickname.Length <= maxChars
            ? withoutNickname
            : withoutNickname.Substring(0, maxChars);
    }
}
