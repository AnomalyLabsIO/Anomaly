using System.Reflection;
using System.Text.Json;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Shared.Networking.Messages;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMapGeneration;
using Il2CppMirror;
using UnityEngine;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

/// <summary>Opt-in incompatibility injection for a disposable staged client process.</summary>
internal static class FacilityCatalogFailureProbe
{
    private const string Owner = "anomaly.smoke.facility-catalog-failure";
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly List<MethodBase> Targets = new();
    private static HarmonyLib.Harmony _harmony;
    private static string _evidence;
    private static bool _attemptStarted;
    private static bool _injected;
    private static string _rejection;
    private static double _rejectedAt;
    private static int _readySucceeded;
    private static int _addPlayerSucceeded;
    private static int _atlasCalls;
    private static int _materializeCalls;
    private static bool _readyAtRejection;
    private static bool _mapGeneratedAtRejection;
    private static int _roomsAtRejection;
    private static bool _gameplayAdmissionObserved;
    private static FacilityGenerationMode? _snapshotMode;
    private static ulong _generation;
    private static AtlasZoneGenerator _generator;
    private static Il2CppReferenceArray<SpawnableRoom> _originalCandidates;

    internal static bool Enabled { get; private set; }
    internal static bool RejectedAndDisconnected => _rejection != null &&
        Time.realtimeSinceStartupAsDouble >= _rejectedAt + 2 && !ApiClient.IsConnected && !NetworkClient.isConnected;

    internal static void Initialize(string evidence)
    {
        Enabled = Environment.GetEnvironmentVariable("ANOMALY_CLIENT_CATALOG_FAILURE_PROBE") == "1";
        if (!Enabled) return;
        string stagingPath = Path.Combine(Environment.CurrentDirectory, "smoke-staging.json");
        if (!File.Exists(stagingPath)) throw new InvalidOperationException("Catalog injection requires a privately staged smoke client.");
        using var staging = JsonDocument.Parse(File.ReadAllText(stagingPath));
        if (!string.Equals(Path.GetFullPath(staging.RootElement.GetProperty("destination").GetString()),
            Path.GetFullPath(Environment.CurrentDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Catalog injection staging destination does not match this process.");
        _evidence = evidence;
        ClientEvents.Ready += ObserveSceneReady;
        _harmony = new HarmonyLib.Harmony(Owner);
        Patch(AccessTools.Method(typeof(SeedSynchronizer), nameof(SeedSynchronizer.Awake)), nameof(InjectBeforeSceneReady), null, Priority.First);
        Patch(AccessTools.Method(Runtime, "FailGeneration"), nameof(ObserveRejection), null);
        Patch(AccessTools.Method(typeof(NetworkClient), nameof(NetworkClient.Ready)), null, nameof(ObserveReady));
        Patch(AccessTools.Method(typeof(NetworkClient), nameof(NetworkClient.AddPlayer)), null, nameof(ObserveAddPlayer));
        Patch(AccessTools.Method(typeof(AtlasZoneGenerator), nameof(AtlasZoneGenerator.Generate)), nameof(ObserveAtlasGeneration), null, Priority.First);
        var generation = Type.GetType("Anomaly.Client.Internal.Map.FacilityGeneration, Anomaly.Client", true);
        Patch(AccessTools.Method(generation, "Materialize"), nameof(ObserveMaterialization), null);
    }

    internal static void BeginAttempt() => _attemptStarted = true;

    internal static void Shutdown()
    {
        ClientEvents.Ready -= ObserveSceneReady;
        if (_generator != null && _originalCandidates != null) _generator.CompatibleRooms = _originalCandidates;
        if (_harmony != null)
            foreach (var target in Targets) _harmony.Unpatch(target, HarmonyPatchType.All, Owner);
        Targets.Clear();
        _harmony = null;
        _generator = null;
        _originalCandidates = null;
        Enabled = false;
    }

    private static Type Runtime => Type.GetType("Anomaly.Client.Internal.Map.FacilityRecipeClient, Anomaly.Client", true);
    private static bool IsReady => (bool)Runtime.GetProperty("IsReady", Flags).GetValue(null);

    private static void Patch(MethodBase target, string prefix, string postfix, int priority = Priority.Normal)
    {
        if (target == null) throw new MissingMethodException("Catalog probe target was unavailable.");
        HarmonyMethod Handler(string method) => method == null ? null : new HarmonyMethod(
            AccessTools.Method(typeof(FacilityCatalogFailureProbe), method)) { priority = priority };
        _harmony.Patch(target, Handler(prefix), Handler(postfix));
        Targets.Add(target);
    }

    private static void InjectBeforeSceneReady()
    {
        if (!_attemptStarted || _injected || NetworkServer.active) return;
        _generator = UnityEngine.Object.FindObjectsByType<AtlasZoneGenerator>(FindObjectsSortMode.None)
            .First(generator => generator.TargetZone == FacilityZone.LightContainment && generator.CompatibleRooms.Length > 1);
        _originalCandidates = _generator.CompatibleRooms;
        _generator.CompatibleRooms = _originalCandidates.Take(_originalCandidates.Length - 1).ToArray();
        _injected = true;
        File.WriteAllText(Path.Combine(_evidence, "catalog-injection.json"), JsonSerializer.Serialize(new
        {
            Injection = "Shortened in-memory LCZ catalog in SeedSynchronizer.Awake prefix before facility scene readiness",
            OriginalCount = _originalCandidates.Length,
            AlteredCount = _generator.CompatibleRooms.Length,
            MapGenerated = SeedSynchronizer.MapGenerated,
            Ready = IsReady,
        }));
    }

    private static void ObserveRejection(Exception exception)
    {
        if (!_attemptStarted || _rejection != null) return;
        _rejection = exception.ToString();
        _rejectedAt = Time.realtimeSinceStartupAsDouble;
        _readyAtRejection = IsReady;
        _mapGeneratedAtRejection = SeedSynchronizer.MapGenerated;
        _roomsAtRejection = UnityEngine.Object.FindObjectsByType<AtlasZoneGenerator>(FindObjectsSortMode.None)
            .Sum(generator => generator.Spawned.Count);
        var snapshot = (FacilitySnapshotMessage)Runtime.GetProperty("CurrentSnapshot", Flags).GetValue(null);
        _snapshotMode = snapshot?.Mode;
        _generation = snapshot?.GenerationId ?? 0;
        File.WriteAllText(Path.Combine(_evidence, "catalog-rejection.json"), JsonSerializer.Serialize(new
        {
            Exception = _rejection, Mode = _snapshotMode?.ToString(), GenerationId = _generation,
            Ready = _readyAtRejection, MapGenerated = _mapGeneratedAtRejection, Rooms = _roomsAtRejection,
        }));
    }

    private static void ObserveReady(bool __result) { if (_attemptStarted && __result) _readySucceeded++; }
    private static void ObserveAddPlayer(bool __result) { if (_attemptStarted && __result) _addPlayerSucceeded++; }
    private static void ObserveAtlasGeneration() { if (_attemptStarted) _atlasCalls++; }
    private static void ObserveMaterialization() { if (_attemptStarted) _materializeCalls++; }
    private static void ObserveSceneReady(ClientReadyEventArgs _) => ObserveClientState();

    internal static void ObserveClientState()
    {
        if (_attemptStarted)
            _gameplayAdmissionObserved |= ApiClient.LocalPlayer != null || !string.IsNullOrEmpty(ApiClient.EnrollmentId);
    }

    internal static bool WriteResult(int readyEvents)
    {
        ObserveClientState();
        var checks = new Dictionary<string, bool>
        {
            ["Catalog was altered before scene readiness"] = _injected,
            ["Authenticated planned snapshot reached validation"] = _snapshotMode == FacilityGenerationMode.Planned && _generation != 0,
            ["Validation rejected the incompatible prefab catalog"] = _rejection?.Contains("Facility prefab catalog differs in zone", StringComparison.Ordinal) == true,
            ["Facility readiness was never released at rejection"] = !_readyAtRejection && !IsReady,
            ["Mirror Ready never succeeded"] = _readySucceeded == 0,
            ["Mirror AddPlayer never succeeded"] = _addPlayerSucceeded == 0,
            ["No local player hub or enrollment was observed"] = !_gameplayAdmissionObserved,
            ["Native completion remained false"] = !_mapGeneratedAtRejection && !SeedSynchronizer.MapGenerated,
            ["No native atlas fallback ran"] = _atlasCalls == 0,
            ["No planned materialization ran"] = _materializeCalls == 0,
            ["No atlas room was materialized"] = _roomsAtRejection == 0,
            ["Connection reached terminal disconnect"] = RejectedAndDisconnected,
        };
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(_evidence, "catalog-failure-probe.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, Checks = checks, ReadySuccesses = _readySucceeded, AddPlayerSuccesses = _addPlayerSucceeded,
            AtlasCalls = _atlasCalls, MaterializeCalls = _materializeCalls, SceneReadyEvents = readyEvents,
            GenerationId = _generation, Rejection = _rejection,
        }));
        return passed;
    }
}

public sealed partial class Core
{
    private void EnqueueFacilityCatalogFailureProbe()
    {
        string server = Environment.GetEnvironmentVariable("ANOMALY_SMOKE_SERVER");
        if (!Uri.TryCreate("udp://" + server, UriKind.Absolute, out var endpoint) ||
            !System.Net.IPAddress.TryParse(endpoint.Host, out var ip) || !System.Net.IPAddress.IsLoopback(ip))
            throw new InvalidOperationException("Catalog incompatibility probe requires an explicit loopback endpoint.");
        Add("catalog-probe-connect", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            {
                Input().SetTextWithoutNotify("Catalog Failure Smoke");
                Submit();
            }
            FacilityCatalogFailureProbe.BeginAttempt();
            ApiClient.MainMenu.ChangeMenu(0);
            ApiClient.Connect(server);
        });
        AddWait("catalog-probe-terminal-rejection", () =>
        {
            PumpServerApiConnection(1, false, 0);
            // Public Ready reports the scene callback, which can occur before a
            // player hub or facility validation. Observe actual admission instead.
            FacilityCatalogFailureProbe.ObserveClientState();
            return FacilityCatalogFailureProbe.RejectedAndDisconnected;
        }, 150);
        Add("catalog-probe-result", () =>
        {
            Require(FacilityCatalogFailureProbe.WriteResult(_readyEvents), "Catalog mismatch rejects generation before Ready/AddPlayer or native fallback");
            File.WriteAllText(Path.Combine(_output, "result.txt"), "PASS: authenticated planned catalog mismatch failed closed before room generation or gameplay readiness.\n");
            Application.Quit();
        });
    }
}
