using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MelonLoader;
using MelonLoader.InternalUtils;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(Anomaly.LaunchProfiler.Core), "Anomaly Launch Profiler", "1.0.0", "AnomalyLabs", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: MelonPriority(int.MinValue)]
[assembly: HarmonyDontPatchAll]

namespace Anomaly.LaunchProfiler;

// The early plugin must not resolve game, Unity or Anomaly types. Their code lives
// in separate methods/files and is first entered after the loader support boundary.
public sealed class Core : MelonPlugin
{
    private static TraceRecorder? _trace;
    private static long _firstPluginQpc;
    private static long _menuReadyQpc;
    private static bool _completed;
    private static bool _runtimeReady;
    private static bool _runtimeAttempted;
    private static bool _updated;
    public static bool Enabled => _trace != null;
    public static bool IsCompleted => _completed;
    public static string OutputDirectory { get; private set; } = "";
    public static string Mode { get; private set; } = "milestones";
    public static string Experiment { get; private set; } = "baseline";
    public static string RunId { get; private set; } = "";

    public override void OnEarlyInitializeMelon()
    {
        _firstPluginQpc = Stopwatch.GetTimestamp();
        var output = Environment.GetEnvironmentVariable("ANOMALY_LAUNCH_PROFILE_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        // Accidental installation in the normal client is inert, even with a stale
        // environment variable: the runner owns an explicit private staging marker.
        string game = Path.GetFullPath(MelonEnvironment.GameRootDirectory);
        string marker = Path.Combine(game, "launch-profiler-staging.json");
        if (!File.Exists(marker)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(marker));
        if (!document.RootElement.TryGetProperty("destination", out var destination) ||
            !string.Equals(Path.GetFullPath(destination.GetString()!), game, StringComparison.OrdinalIgnoreCase))
            return;
        OutputDirectory = Path.GetFullPath(output);
        Directory.CreateDirectory(OutputDirectory);
        Mode = Environment.GetEnvironmentVariable("ANOMALY_LAUNCH_PROFILE_MODE") ?? "milestones";
        Experiment = Environment.GetEnvironmentVariable("ANOMALY_LAUNCH_PROFILE_EXPERIMENT") ?? "baseline";
        RunId = Environment.GetEnvironmentVariable("ANOMALY_LAUNCH_PROFILE_RUN_ID") ?? Guid.NewGuid().ToString("N");
        _trace = new TraceRecorder();
        _trace.Add("plugin.early", _firstPluginQpc, detail: new { mode = Mode, experiment = Experiment });
        MelonEvents.OnSceneWasLoaded.Subscribe((index, name) => Mark("scene.loaded", new { index, name }));
        MelonEvents.OnSceneWasInitialized.Subscribe((index, name) => {
            Mark("scene.initialized", new { index, name });
            if (_runtimeReady) GameProbe.HandleSceneInitialized(index, name);
        });
    }

    public override void OnPreInitialization() => Mark("loader.pre-initialization");
    public override void OnApplicationEarlyStart() => Mark("loader.application-early-start");
    public override void OnPreModsLoaded() => Mark("loader.pre-mods");
    public override void OnPreSupportModule() => Mark("loader.pre-support");

    public override void OnInitializeMelon()
    {
        if (!Enabled) return;
        Mark("loader.application-start", new
        {
            game = UnityInformationHandler.GameVersion,
            loader = typeof(MelonPlugin).Assembly.GetName().Version?.ToString(),
        });
        if (Mode is not ("milestones" or "detailed")) { Fail("Unknown capture mode: " + Mode); return; }
        if (UnityInformationHandler.GameVersion != "14.2.7")
        {
            Fail("Unsupported game version; version-specific hooks were not installed.");
            return;
        }
        InitializeRuntime();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InitializeRuntime()
    {
        _runtimeAttempted = true;
        long start = BeginSpan("profiler.install");
        try
        {
            ExperimentHooks.Initialize();
            if (Mode == "detailed")
            {
                ManagedMethodProbes.Initialize();
                GameMethodProbes.Initialize();
            }
            GameProbe.Initialize();
            _runtimeReady = true;
        }
        catch (Exception exception) { Fail("Profiler initialization: " + exception); }
        finally { EndSpan("profiler.install", start); }
    }

    public override void OnLateInitializeMelon() => Mark("loader.first-start");
    public override void OnUpdate()
    {
        if (!Enabled || !_runtimeReady) return;
        if (!_updated) { _updated = true; Mark("game.first-update"); }
        try
        {
            ExperimentHooks.Update();
            GameProbe.Update();
        }
        catch (Exception exception) { Fail("Profiler update: " + exception); }
    }

    public override void OnApplicationQuit()
    {
        if (!Enabled) return;
        Mark("game.quit");
        if (!_completed) Fail("Game quit before menu/settings verification completed.");
        try
        {
            if (_runtimeAttempted)
            {
                Teardown("managed", ManagedMethodProbes.Shutdown);
                Teardown("game-methods", GameMethodProbes.Shutdown);
                Teardown("game-readiness", GameProbe.Shutdown);
                Teardown("experiments", ExperimentHooks.Shutdown);
            }
        }
        finally
        {
            // Include incomplete coroutine lifetimes and deferred shutdown work,
            // without changing the already recorded completion time.
            _trace!.Write(OutputDirectory, RunId, Mode, Experiment, _firstPluginQpc);
        }
    }

    private static void Teardown(string component, Action action)
    {
        try { action(); }
        catch (Exception exception) { Mark("profiler.teardown.failure", new { component, error = exception.GetType().Name }); }
    }

    public static void Mark(string name, object? detail = null)
    {
        long now = Stopwatch.GetTimestamp();
        if (name == "menu.ready" && _menuReadyQpc == 0) _menuReadyQpc = now;
        _trace?.Add(name, now, detail: detail);
    }

    public static long BeginSpan(string name) => Stopwatch.GetTimestamp();
    public static void EndSpan(string name, long start, object? detail = null) =>
        _trace?.Add(name, start, Stopwatch.GetTimestamp() - start, detail, span: true);

    public static void Complete(object readiness)
    {
        if (_completed) return;
        if (_menuReadyQpc == 0) { Fail("Completion had no usable-menu marker."); return; }
        Finish(true, null, readiness);
    }

    public static void Fail(string reason)
    {
        if (!Enabled || _completed) return;
        Mark("profiler.failure", new { reason });
        Finish(false, reason, null);
        MelonLogger.Warning("Launch profiling failed: " + reason);
    }

    private static void Finish(bool success, string? failure, object? readiness)
    {
        _completed = true;
        long completed = Stopwatch.GetTimestamp();
        _trace!.Write(OutputDirectory, RunId, Mode, Experiment, _firstPluginQpc);
        TraceRecorder.WriteJson(Path.Combine(OutputDirectory, "result.json"), new
        {
            schemaVersion = 1, success, failure, mode = Mode, experiment = Experiment,
            runId = RunId, pid = Environment.ProcessId, stopwatchFrequency = Stopwatch.Frequency,
            firstPluginQpc = _firstPluginQpc, menuReadyQpc = _menuReadyQpc, completedQpc = completed,
            droppedEvents = _trace.Dropped, readiness,
        });
    }
}
