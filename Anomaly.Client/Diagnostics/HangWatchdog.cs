using System.Diagnostics;
using Anomaly.Client.API.Features;
using MelonLoader;

namespace Anomaly.Client.Diagnostics;

internal static class HangWatchdog
{
    private const int CheckIntervalMs = 2000;
    private const int HangReportThresholdSeconds = 15;
    private const int HangDumpThresholdSeconds = 30;
    private const int HangPermanentThresholdSeconds = 60;

    private static long _lastHeartbeatTicks;
    private static volatile bool _running;
    private static Thread _thread;

    private static bool _hangReported;
    private static bool _hangDumped;
    private static bool _hangPermanentLogged;

    internal static void Start()
    {
        Interlocked.Exchange(ref _lastHeartbeatTicks, Stopwatch.GetTimestamp());
        _running = true;
        _hangReported = false;
        _hangDumped = false;
        _hangPermanentLogged = false;

        _thread = new Thread(WatchdogLoop)
        {
            Name = "AnomalyHangWatchdog",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        _thread.Start();
    }

    internal static void Heartbeat()
    {
        Interlocked.Exchange(ref _lastHeartbeatTicks, Stopwatch.GetTimestamp());

        // Reset escalation flags when the main thread resumes
        if (_hangReported)
        {
            _hangReported = false;
            _hangDumped = false;
            _hangPermanentLogged = false;
            MelonLogger.Warning("HangWatchdog: Main thread resumed after hang.");
        }
    }

    internal static void Stop()
    {
        _running = false;
        // Thread is background, will die with the process. No need to join.
    }

    private static void WatchdogLoop()
    {
        while (_running)
        {
            Thread.Sleep(CheckIntervalMs);

            if (!_running)
                break;

            long elapsed = Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastHeartbeatTicks);
            double elapsedSeconds = (double)elapsed / Stopwatch.Frequency;

            // Escalation level 1: Write crash report
            if (elapsedSeconds >= HangReportThresholdSeconds && !_hangReported)
            {
                _hangReported = true;
                WriteHangReport(elapsedSeconds);
            }

            // Escalation level 2: Create minidump
            if (elapsedSeconds >= HangDumpThresholdSeconds && !_hangDumped)
            {
                _hangDumped = true;
                WriteHangDump();
            }

            // Escalation level 3: Log permanent warning
            if (elapsedSeconds >= HangPermanentThresholdSeconds && !_hangPermanentLogged)
            {
                _hangPermanentLogged = true;
                MelonLogger.Error(
                    $"HangWatchdog: Main thread has been unresponsive for {elapsedSeconds:F0}s. " +
                    "Process may be permanently hung.");
            }
        }
    }

    private static void WriteHangReport(double elapsedSeconds)
    {
        try
        {
            var report = new CrashReport
            {
                ReportId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                TriggerType = "HangDetected",
                ExceptionMessage = $"Main thread unresponsive for {elapsedSeconds:F1} seconds",
                RecentLogEntries = CircularLogBuffer.GetSnapshot(),
                SystemInfo = DiagnosticCollector.GetSystemInfo(),
                GameState = DiagnosticCollector.GetGameState(),
                LoadedMods = DiagnosticCollector.GetLoadedMods(),
                LastSuccessfulFrame = DiagnosticBootstrap.LastSuccessfulFrame,
                FrameCount = DiagnosticBootstrap.FrameCount
            };

            CrashReportWriter.Write(report);
            MelonLogger.Warning($"HangWatchdog: Main thread unresponsive for {elapsedSeconds:F0}s. Crash report written.");
        }
        catch { /* watchdog must not throw */ }
    }

    private static void WriteHangDump()
    {
        try
        {
            string dir = Paths.CrashReportsPath;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string guid = Guid.NewGuid().ToString("N").Substring(0, 6);
            string dumpPath = Path.Combine(dir, $"crash-{timestamp}-HangDetected-{guid}.dmp");

            if (NativeMethods.WriteMiniDump(dumpPath, NativeMethods.MiniDumpNormal | NativeMethods.MiniDumpWithThreadInfo))
            {
                MelonLogger.Warning($"HangWatchdog: Minidump written to {dumpPath}");
            }
            else
            {
                MelonLogger.Error("HangWatchdog: Failed to write minidump.");
            }
        }
        catch { /* watchdog must not throw */ }
    }
}
