using Anomaly.Client.Api.Features;
using Anomaly.Client.Diagnostics.News;
using MelonLoader;

namespace Anomaly.Client.Diagnostics;

public static class DiagnosticBootstrap
{
    private static int _initialized;
    internal static DateTime LastSuccessfulFrame { get; private set; }
    internal static long FrameCount { get; private set; }

    private static readonly string CleanShutdownMarkerPath =
        Path.Combine(Paths.CrashReportsPath, "last-clean-shutdown.marker");

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        try
        {
            // Ensure crash reports directory exists
            if (!Directory.Exists(Paths.CrashReportsPath))
                Directory.CreateDirectory(Paths.CrashReportsPath);

            // Check for previous unclean exit
            if (File.Exists(CleanShutdownMarkerPath))
            {
                NewsDiagnosticService.ObserveCleanShutdownMarker(File.GetLastWriteTimeUtc(CleanShutdownMarkerPath));
                File.Delete(CleanShutdownMarkerPath);
                MelonLogger.Msg("Diagnostics: Previous session exited cleanly.");
            }
            else if (Directory.Exists(Paths.CrashReportsPath) &&
                     Directory.GetFiles(Paths.CrashReportsPath).Length > 0)
            {
                MelonLogger.Warning("Diagnostics: Previous session may not have exited cleanly. Check CrashReports/.");
            }

            // Initialize components
            DiagnosticCollector.CacheSystemInfo();
            CrashHooks.RegisterAll();
            HangWatchdog.Start();

            // Cleanup old reports
            CrashReportWriter.CleanupOldReports();

            LastSuccessfulFrame = DateTime.UtcNow;
            FrameCount = 0;

            MelonLogger.Msg($"Diagnostics: Watchdog and crash handlers initialized. {DiagnosticPrivacyPolicy.MiniDumpStatus}");
            MelonLogger.Warning($"Diagnostics: {DiagnosticPrivacyPolicy.ReviewBeforeShareNotice}");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Diagnostics: Failed to initialize: {ex}");
            HangWatchdog.Stop();
            Interlocked.Exchange(ref _initialized, 0);
        }
    }

    public static void Heartbeat()
    {
        LastSuccessfulFrame = DateTime.UtcNow;
        FrameCount++;
        HangWatchdog.Heartbeat();
        DiagnosticCollector.RefreshGameState();
    }

    public static void Shutdown()
    {
        if (Interlocked.Exchange(ref _initialized, 0) == 0)
            return;

        try
        {
            HangWatchdog.Stop();

            // Write clean-shutdown marker
            if (!Directory.Exists(Paths.CrashReportsPath))
                Directory.CreateDirectory(Paths.CrashReportsPath);

            File.WriteAllText(CleanShutdownMarkerPath, DateTime.UtcNow.ToString("O"));

            MelonLogger.Msg("Diagnostics: Clean shutdown recorded.");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Diagnostics: Shutdown error: {ex.Message}");
        }
    }
}
