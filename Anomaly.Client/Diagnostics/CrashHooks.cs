using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Anomaly.Client.API.Features;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Diagnostics;

internal static class CrashHooks
{
    private static int _isHandlingCrash;

    // First-chance exception flood detection
    private static readonly ConcurrentDictionary<string, FloodTracker> _floodTrackers = new();
    private const int FloodThreshold = 50;
    private static readonly TimeSpan FloodWindow = TimeSpan.FromSeconds(10);

    // SEH filter must be kept alive to prevent GC collection
    private static NativeMethods.UnhandledExceptionFilterDelegate _sehFilterDelegate;

    internal static void RegisterAll()
    {
        RegisterManaged();
        RegisterUnityLog();
        RegisterNative();
    }

    private static void RegisterManaged()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    private static void RegisterUnityLog()
    {
        try
        {
            // In Il2Cpp, Unity's Application events are accessed via add_ methods
            Application.add_logMessageReceived(
                new Action<string, string, LogType>(OnUnityLogMessage));
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"CrashHooks: Failed to register Unity log callback: {ex.Message}");
        }
    }

    private static void RegisterNative()
    {
        try
        {
            _sehFilterDelegate = SehFilter;
            NativeMethods.SetUnhandledExceptionFilter(_sehFilterDelegate);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"CrashHooks: Failed to register SEH filter: {ex.Message}");
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (Interlocked.CompareExchange(ref _isHandlingCrash, 1, 0) != 0)
            return;

        try
        {
            var ex = args.ExceptionObject as Exception;
            var report = BuildReport("UnhandledException", ex);
            CrashReportWriter.Write(report);

            if (args.IsTerminating)
            {
                string dumpPath = GetDumpPath(report);
                NativeMethods.WriteMiniDump(dumpPath, NativeMethods.MiniDumpNormal);
                report.MiniDumpPath = dumpPath;
            }
        }
        catch { /* crash handler must not throw */ }
        finally
        {
            Interlocked.Exchange(ref _isHandlingCrash, 0);
        }
    }

    private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
    {
        if (Interlocked.CompareExchange(ref _isHandlingCrash, 1, 0) != 0)
            return;

        try
        {
            var report = BuildReport("TaskException", args.Exception?.Flatten());
            CrashReportWriter.Write(report);
            args.SetObserved();
        }
        catch { /* crash handler must not throw */ }
        finally
        {
            Interlocked.Exchange(ref _isHandlingCrash, 0);
        }
    }

    private static void OnFirstChanceException(object sender, FirstChanceExceptionEventArgs args)
    {
        // Feed log buffer, don't write report unless flooding
        string typeName = args.Exception?.GetType().FullName ?? "Unknown";

        var tracker = _floodTrackers.GetOrAdd(typeName, new FloodTracker());
        tracker.Increment();

        if (tracker.Count == FloodThreshold && tracker.IsWithinWindow)
        {
            try
            {
                var report = BuildReport("FirstChanceFlood", args.Exception);
                report.ExceptionMessage = $"Exception '{typeName}' fired {FloodThreshold}+ times in {FloodWindow.TotalSeconds}s";
                CrashReportWriter.Write(report);
            }
            catch { /* best effort */ }
        }
    }

    private static void OnUnityLogMessage(string message, string stackTrace, LogType type)
    {
        // Always feed the circular log buffer
        string level;
        if (type == LogType.Error) level = "ERROR";
        else if (type == LogType.Exception) level = "EXCEPTION";
        else if (type == LogType.Assert) level = "ASSERT";
        else if (type == LogType.Warning) level = "WARN";
        else level = "INFO";
        CircularLogBuffer.Add(level, message);

        // Write crash reports for exceptions and asserts
        if (type == LogType.Exception || type == LogType.Assert)
        {
            if (Interlocked.CompareExchange(ref _isHandlingCrash, 1, 0) != 0)
                return;

            try
            {
                var report = new CrashReport
                {
                    ReportId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    TriggerType = type == LogType.Exception ? "UnityException" : "UnityAssert",
                    ExceptionType = "Unity." + type,
                    ExceptionMessage = message,
                    StackTrace = stackTrace,
                    RecentLogEntries = CircularLogBuffer.GetSnapshot(),
                    SystemInfo = DiagnosticCollector.GetSystemInfo(),
                    GameState = DiagnosticCollector.GetGameState(),
                    LoadedMods = DiagnosticCollector.GetLoadedMods(),
                    LastSuccessfulFrame = DiagnosticBootstrap.LastSuccessfulFrame,
                    FrameCount = DiagnosticBootstrap.FrameCount
                };
                CrashReportWriter.Write(report);
            }
            catch { /* crash handler must not throw */ }
            finally
            {
                Interlocked.Exchange(ref _isHandlingCrash, 0);
            }
        }
    }

    private static int SehFilter(IntPtr exceptionPointers)
    {
        try
        {
            string dir = Paths.CrashReportsPath;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string guid = Guid.NewGuid().ToString("N").Substring(0, 6);
            string dumpPath = Path.Combine(dir, $"crash-{timestamp}-NativeCrash-{guid}.dmp");
            string textPath = Path.Combine(dir, $"crash-{timestamp}-NativeCrash-{guid}.txt");

            NativeMethods.WriteMiniDump(dumpPath, NativeMethods.MiniDumpNormal, exceptionPointers);

            // Write minimal text report (managed serialization may not be safe)
            File.WriteAllText(textPath,
                $"Native crash at {DateTime.UtcNow:O}\nMiniDump: {dumpPath}\n");
        }
        catch { /* SEH handler must not throw */ }

        return NativeMethods.EXCEPTION_EXECUTE_HANDLER;
    }

    private static CrashReport BuildReport(string triggerType, Exception ex)
    {
        return new CrashReport
        {
            ReportId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            TriggerType = triggerType,
            ExceptionType = ex?.GetType().FullName,
            ExceptionMessage = ex?.Message,
            StackTrace = ex?.ToString(),
            RecentLogEntries = CircularLogBuffer.GetSnapshot(),
            SystemInfo = DiagnosticCollector.GetSystemInfo(),
            GameState = DiagnosticCollector.GetGameState(),
            LoadedMods = DiagnosticCollector.GetLoadedMods(),
            LastSuccessfulFrame = DiagnosticBootstrap.LastSuccessfulFrame,
            FrameCount = DiagnosticBootstrap.FrameCount
        };
    }

    private static string GetDumpPath(CrashReport report)
    {
        string dir = Paths.CrashReportsPath;
        string timestamp = report.Timestamp.ToString("yyyyMMdd-HHmmss");
        string guid = report.ReportId.Substring(0, 6);
        return Path.Combine(dir, $"crash-{timestamp}-{report.TriggerType}-{guid}.dmp");
    }

    private class FloodTracker
    {
        private int _count;
        private DateTime _windowStart = DateTime.UtcNow;

        public int Count => _count;
        public bool IsWithinWindow => (DateTime.UtcNow - _windowStart) < FloodWindow;

        public void Increment()
        {
            if (!IsWithinWindow)
            {
                _count = 0;
                _windowStart = DateTime.UtcNow;
            }
            Interlocked.Increment(ref _count);
        }
    }
}
