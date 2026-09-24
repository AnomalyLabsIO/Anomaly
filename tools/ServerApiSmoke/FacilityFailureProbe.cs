using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Anomaly.Server.Api.Events.Handlers;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using MapGeneration;
using UnityEngine;

namespace Anomaly.ServerApiSmoke;

/// <summary>Startup-only destructive-in-memory fixture for a disposable, isolated server process.</summary>
internal static class FacilityFailureProbe
{
    private static string _evidence;
    private static bool _injected;
    private static bool _captured;
    private static float _deadline;
    private static int _mapGenerated;
    private static int _recipeApplied;
    private static AtlasZoneGenerator _generator;
    private static SpawnableRoom[] _originalCandidates;

    internal static bool Enabled { get; private set; }

    internal static void Initialize(string evidence)
    {
        Enabled = Environment.GetEnvironmentVariable("ANOMALY_SERVER_SMOKE_FAILURE_PROBE") == "1";
        if (!Enabled) return;
        _evidence = evidence;
        _injected = _captured = false;
        _mapGenerated = _recipeApplied = 0;
        _deadline = Time.realtimeSinceStartup + 90f;
        ServerEvents.MapGenerated += OnMapGenerated;
        MapEvents.RecipeApplied += OnRecipeApplied;
        StaticUnityMethods.OnUpdate += Tick;
    }

    internal static void Inject(AtlasZoneGenerator generator)
    {
        if (!Enabled || _injected) throw new InvalidOperationException("Failure injection is limited to one explicitly enabled startup.");
        if (generator.CompatibleRooms.Length < 2) throw new InvalidOperationException("Failure probe needs a nontrivial room catalog.");
        _generator = generator;
        _originalCandidates = generator.CompatibleRooms;
        generator.CompatibleRooms = _originalCandidates.Take(_originalCandidates.Length - 1).ToArray();
        _injected = true;
        _deadline = Time.realtimeSinceStartup + 1f;
    }

    internal static void Shutdown()
    {
        StaticUnityMethods.OnUpdate -= Tick;
        ServerEvents.MapGenerated -= OnMapGenerated;
        MapEvents.RecipeApplied -= OnRecipeApplied;
        if (_generator != null && _originalCandidates != null) _generator.CompatibleRooms = _originalCandidates;
        _generator = null;
        _originalCandidates = null;
        Enabled = false;
    }

    private static void OnMapGenerated(MapGeneratedEventArgs _) => _mapGenerated++;
    private static void OnRecipeApplied() => _recipeApplied++;

    private static void Tick()
    {
        if (_captured || Time.realtimeSinceStartup < _deadline) return;
        _captured = true;
        StaticUnityMethods.OnUpdate -= Tick;
        var assertions = new List<object>();
        bool passed = true;
        string error = null;
        void Require(string name, bool condition)
        {
            assertions.Add(new { Name = name, Passed = condition });
            passed &= condition;
        }
        try
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            Type runtime = typeof(Anomaly.Server.Plugin).Assembly.GetType("Anomaly.Server.Internal.Map.FacilityLayoutRuntime", true);
            bool generationFailed = (bool)runtime.GetProperty("GenerationFailed", flags).GetValue(null);
            var snapshotArguments = new object[] { null };
            bool snapshotAvailable = (bool)runtime.GetMethod("TryGetSnapshot", flags).Invoke(null, snapshotArguments);
            bool published = (bool)runtime.GetField("_published", flags).GetValue(null);
            int materializedRooms = UnityEngine.Object.FindObjectsByType<AtlasZoneGenerator>(FindObjectsSortMode.None)
                .Sum(generator => generator.Spawned.Count);
            Require("Catalog failure was injected from LayoutFinalizing", _injected);
            Require("Generation failure was recorded", generationFailed);
            Require("Native map completion remains false", !SeedSynchronizer.MapGenerated);
            Require("Snapshot lookup returns no published snapshot", !snapshotAvailable && snapshotArguments[0] == null);
            Require("Snapshot publication remains false", !published);
            Require("MapGenerated was never observed", _mapGenerated == 0);
            Require("RecipeApplied was never observed", _recipeApplied == 0);
            Require("No atlas rooms were materialized", materializedRooms == 0);
        }
        catch (Exception exception)
        {
            passed = false;
            error = exception.ToString();
        }
        File.WriteAllText(Path.Combine(_evidence, "failure-probe.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, Injection = "shortened in-memory room catalog after planning", Injected = _injected,
            ObservedMapGenerated = _mapGenerated, ObservedRecipeApplied = _recipeApplied,
            Assertions = assertions, Error = error
        }));
        File.WriteAllText(Path.Combine(_evidence, "failure-probe-result.txt"), passed
            ? "PASS: rejected generation produced no rooms, completion events or published snapshot."
            : "FAIL: inspect failure-probe.json for missing failure guarantees.");
        if (!passed) File.WriteAllText(Path.Combine(_evidence, "failure.txt"), "Facility failure probe failed: " + error);
    }
}
