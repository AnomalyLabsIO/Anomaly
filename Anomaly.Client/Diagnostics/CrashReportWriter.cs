using System.Text.Json;
using Anomaly.Client.API.Features;
using MelonLoader;

namespace Anomaly.Client.Diagnostics;

public static class CrashReportWriter
{
    private const int MaxReports = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string Write(CrashReport report)
    {
        try
        {
            string dir = Paths.CrashReportsPath;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 6);
            string timestamp = report.Timestamp.ToString("yyyyMMdd-HHmmss");
            string fileName = $"crash-{timestamp}-{report.TriggerType}-{shortGuid}.json";
            string filePath = Path.Combine(dir, fileName);

            string json = JsonSerializer.Serialize(report, JsonOptions);
            File.WriteAllText(filePath, json);

            MelonLogger.Warning($"Crash report written: {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"CrashReportWriter: Failed to write report: {ex.Message}");
            return null;
        }
    }

    public static void CleanupOldReports()
    {
        try
        {
            string dir = Paths.CrashReportsPath;
            if (!Directory.Exists(dir))
                return;

            var files = Directory.GetFiles(dir, "crash-*.json")
                .OrderByDescending(f => File.GetCreationTimeUtc(f))
                .ToArray();

            if (files.Length <= MaxReports)
                return;

            for (int i = MaxReports; i < files.Length; i++)
            {
                try
                {
                    File.Delete(files[i]);

                    // Also delete matching .dmp if it exists
                    string dmpPath = Path.ChangeExtension(files[i], ".dmp");
                    if (File.Exists(dmpPath))
                        File.Delete(dmpPath);
                }
                catch { /* best effort */ }
            }

            MelonLogger.Msg($"CrashReportWriter: Cleaned up {files.Length - MaxReports} old crash reports.");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"CrashReportWriter: Cleanup failed: {ex.Message}");
        }
    }
}
