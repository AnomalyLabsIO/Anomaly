using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Anomaly.Client.Api.Features;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Diagnostics;

internal static class CrashHooks
{
    private static int _isHandlingCrash;
    private static int _registered;

    // First-chance exception flood detection
    private static readonly ConcurrentDictionary<string, FloodTracker> _floodTrackers = new();
    private const int FloodThreshold = 50;
    private static readonly TimeSpan FloodWindow = TimeSpan.FromSeconds(10);

    // Unity log exception dedup: same signature within the window reports once.
    private static readonly ConcurrentDictionary<string, UnityLogRateLimiter> _unityLogLimiters = new();
    private static readonly TimeSpan UnityLogDedupWindow = TimeSpan.FromSeconds(30);

    // First-chance exception types that are known infrastructure noise —
    // always caught internally by the runtime, surfaced to us only because
    // the FirstChance hook runs before any catch. Don't count these toward
    // flood detection or we'll generate crash reports for nothing.
    private static readonly HashSet<string> BenignFirstChanceTypes = new HashSet<string>
    {
        // MelonLogger.Msg/Warning/Error internally do `new StackTrace(fNeedFileInfo:true)`
        // to identify the calling mod. .NET's StackTraceSymbols.GetSourceLineInfo
        // walks PDBs via System.Reflection.Metadata — any assembly on the stack
        // with a missing/malformed PDB trips BlobReader out-of-bounds. The throw
        // is swallowed (symbolication falls back to no-source), but it fires on
        // every MelonLogger call. Noisy at connect time when many logs happen.
        "System.BadImageFormatException",
    };

    // Well-known vanilla SCP:SL exceptions that self-resolve and shouldn't
    // generate reports at all. Each entry is a substring matched against the
    // first line of the stack trace. Add to this list when a new benign
    // repeat-offender appears in the flood.
    private static readonly string[] VanillaBenignStackSignatures =
    {
        // First-frame Alphawarhead UI access before AlphaWarheadController.Singleton is set
        "AlphaWarheadOutsitePanel.UpdateDisplays",
        "AlphaWarheadController.get_TimeUntilDetonation",
        // PreLoader-phase SDR/NativeHttp type-init cascade. Our central-auth binary
        // patches short-circuit the code that would normally set up the native HTTP
        // module, so SteamDatagramRelayHandler.Start() hits a missing-DLL Win32Exception
        // (mono-io-layer-error 126 / ERROR_MOD_NOT_FOUND) in NativeHttp..cctor. The
        // framework's custom auth replaces SDR entirely, so this is harmless.
        "Utils.Http.SDR.SteamDatagramRelayHandler.Start",
    };

    // SEH filter must be kept alive to prevent GC collection
    private static NativeMethods.UnhandledExceptionFilterDelegate _sehFilterDelegate;

    internal static void RegisterAll()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
            return;

        RegisterManaged();
        RegisterUnityLog();
        if (DiagnosticPrivacyPolicy.MiniDumpsEnabled)
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

            if (args.IsTerminating && DiagnosticPrivacyPolicy.MiniDumpsEnabled)
            {
                Directory.CreateDirectory(Paths.CrashReportsPath);
                string dumpPath = CrashReportWriter.GetArtifactPath(report, ".dmp");
                if (NativeMethods.WriteMiniDump(dumpPath, NativeMethods.MiniDumpNormal))
                    report.MiniDumpPath = Path.GetFileName(dumpPath);
                else
                    TryDelete(dumpPath);
            }

            CrashReportWriter.Write(report);
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

        // Skip known-benign infrastructure noise entirely.
        if (BenignFirstChanceTypes.Contains(typeName))
            return;

        var tracker = _floodTrackers.GetOrAdd(typeName, new FloodTracker());
        tracker.Increment();

        // Capture the managed stack on the FIRST hit per window — the
        // exception's own Stack is often just the throw helper at this
        // point (stack gets populated when it propagates), so the flood
        // report wouldn't tell us WHERE the throw originated without this.
        if (tracker.FirstStackCapture == null)
        {
            try { tracker.FirstStackCapture = new System.Diagnostics.StackTrace(true).ToString(); }
            catch { /* best effort, never throw from the handler */ }
        }

        if (tracker.Count == FloodThreshold && tracker.IsWithinWindow)
        {
            try
            {
                var report = BuildReport("FirstChanceFlood", args.Exception);
                report.ExceptionMessage = $"Exception '{typeName}' fired {FloodThreshold}+ times in {FloodWindow.TotalSeconds}s";
                if (!string.IsNullOrEmpty(tracker.FirstStackCapture))
                {
                    report.StackTrace = (args.Exception?.ToString() ?? "") +
                        "\n\n=== First-occurrence managed stack ===\n" +
                        tracker.FirstStackCapture;
                }
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
            // Skip reports for well-known vanilla SCP:SL bugs that self-
            // heal within a frame or two (e.g., Alphawarhead panel null deref
            // before AlphaWarheadController.Singleton resolves).
            if (IsKnownBenignVanillaException(message, stackTrace))
                return;

            // Dedup: same (message + top-of-stack) within the window gets
            // one report. The FirstN counter lets a handful through so the
            // first instance is always captured, and further recurrences
            // don't flood the disk and console.
            string signature = BuildUnityLogSignature(message, stackTrace);
            // Use the non-lambda GetOrAdd overload — the Func<K,V> variant
            // pulls in nullable-reference-type annotations from its signature
            // and triggers CS0656 when Nullable is disabled on this project.
            var limiter = _unityLogLimiters.GetOrAdd(signature, new UnityLogRateLimiter());
            if (!limiter.ShouldReport())
                return;

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

    private static bool IsKnownBenignVanillaException(string message, string stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace)) return false;
        foreach (var sig in VanillaBenignStackSignatures)
        {
            if (stackTrace.IndexOf(sig, StringComparison.Ordinal) >= 0) return true;
        }

        if (IsLiteNetLibShutdownDisconnect(message, stackTrace))
            return true;

        return false;
    }

    private static bool IsLiteNetLibShutdownDisconnect(string message, string stackTrace)
    {
        if (string.IsNullOrEmpty(message) || message.IndexOf("NullReferenceException", StringComparison.Ordinal) < 0)
            return false;

        if (stackTrace.IndexOf("LiteNetLib4MirrorClient+<>c__DisplayClass9_0.<OnNetworkReceive>b__0", StringComparison.Ordinal) < 0)
            return false;

        foreach (string entry in CircularLogBuffer.GetSnapshot())
        {
            if (entry.IndexOf("Server is shutting down", StringComparison.Ordinal) >= 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Build a stable signature for Unity log exceptions to enable dedup.
    /// Uses the message + the first non-empty stack frame (usually the
    /// topmost method in the exception origin), which is enough to catch
    /// per-frame repeats without merging genuinely different exceptions.
    /// </summary>
    private static string BuildUnityLogSignature(string message, string stackTrace)
    {
        string firstFrame = "";
        if (!string.IsNullOrEmpty(stackTrace))
        {
            int nl = stackTrace.IndexOf('\n');
            firstFrame = (nl >= 0 ? stackTrace.Substring(0, nl) : stackTrace).Trim();
        }
        return (message ?? "") + "|" + firstFrame;
    }

    private static int SehFilter(IntPtr exceptionPointers)
    {
        try
        {
            if (!DiagnosticPrivacyPolicy.MiniDumpsEnabled)
                return NativeMethods.EXCEPTION_EXECUTE_HANDLER;

            string dir = Paths.CrashReportsPath;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var report = new CrashReport
            {
                ReportId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                TriggerType = "NativeCrash"
            };
            string dumpPath = CrashReportWriter.GetArtifactPath(report, ".dmp");
            string textPath = CrashReportWriter.GetArtifactPath(report, ".txt");

            bool dumped = NativeMethods.WriteMiniDump(
                dumpPath,
                NativeMethods.MiniDumpNormal,
                exceptionPointers);
            if (!dumped)
                TryDelete(dumpPath);

            // Write minimal text report (managed serialization may not be safe)
            File.WriteAllText(textPath,
                $"ArtifactId: {report.ReportId}\n" +
                $"Native crash at {report.Timestamp:O}\n" +
                $"MiniDump: {(dumped ? Path.GetFileName(dumpPath) : "not written")}\n" +
                DiagnosticPrivacyPolicy.ReviewBeforeShareNotice + "\n");
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort in crash context.
        }
    }

    private class FloodTracker
    {
        private int _count;
        private DateTime _windowStart = DateTime.UtcNow;
        public string FirstStackCapture;

        public int Count => _count;
        public bool IsWithinWindow => (DateTime.UtcNow - _windowStart) < FloodWindow;

        public void Increment()
        {
            if (!IsWithinWindow)
            {
                _count = 0;
                _windowStart = DateTime.UtcNow;
                FirstStackCapture = null;
            }
            Interlocked.Increment(ref _count);
        }
    }

    /// <summary>
    /// Per-signature rate limiter for Unity log exceptions. Report the
    /// first occurrence in each window; drop the rest. The window resets
    /// when <see cref="UnityLogDedupWindow"/> elapses with no hits.
    /// </summary>
    private class UnityLogRateLimiter
    {
        private DateTime _lastReport = DateTime.MinValue;

        public bool ShouldReport()
        {
            var now = DateTime.UtcNow;
            lock (this)
            {
                if (now - _lastReport < UnityLogDedupWindow) return false;
                _lastReport = now;
                return true;
            }
        }
    }
}
