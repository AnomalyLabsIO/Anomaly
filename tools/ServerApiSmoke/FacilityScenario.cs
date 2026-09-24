using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anomaly.Server.Api.Events.Arguments;
using Anomaly.Server.Api.Events.Handlers;
using Anomaly.Server.Api.Map;
using LabApi.Events.Arguments.ServerEvents;
using MapGeneration;
using UnityEngine;

namespace Anomaly.ServerApiSmoke;

internal static class FacilityScenario
{
    private static string _scenario;
    private static string _activeScenario;
    private static string _baselinePath;
    private static string _evidence;
    private static int _seed;

    internal static void Initialize(string evidence)
    {
        _evidence = evidence;
        _baselinePath = Environment.GetEnvironmentVariable("ANOMALY_SERVER_SMOKE_BASELINE");
        if (string.IsNullOrWhiteSpace(_baselinePath)) _baselinePath = Path.Combine(evidence, "facility-baseline.json");
        _seed = int.TryParse(Environment.GetEnvironmentVariable("ANOMALY_SERVER_SMOKE_SEED"), out int seed) && seed > 0 ? seed : 12345;
        Select(Environment.GetEnvironmentVariable("ANOMALY_SERVER_SMOKE_SCENARIO") ?? "pristine");
        FacilityFailureProbe.Initialize(evidence);
        if (FacilityFailureProbe.Enabled) _scenario = "planned-failure";
    }

    internal static void Select(string scenario)
    {
        if (FacilityFailureProbe.Enabled) throw new InvalidOperationException("The startup failure probe cannot switch facility scenarios.");
        if (scenario != "pristine" && scenario != "noop" && scenario != "lcz" && scenario != "hcz" && scenario != "entrance" && scenario != "atlas" &&
            scenario != "planned-noop" && scenario != "planned-move" && scenario != "planned-remove" && scenario != "planned-checkpoint" && scenario != "planned-recipe-noop")
            throw new ArgumentException("Unknown facility scenario: " + scenario);
        _scenario = scenario;
    }

    internal static void Capture(string json)
    {
        if (_activeScenario == null) return;
        using var capture = JsonDocument.Parse(json);
        File.WriteAllText(Path.Combine(_evidence, "facility-contents-generation-" + capture.RootElement.GetProperty("GenerationId").GetUInt64() + ".json"), FacilityContents.Capture());
        File.WriteAllText(Path.Combine(_evidence, "facility-generation-" + capture.RootElement.GetProperty("GenerationId").GetUInt64() + ".json"), json);
        if (_activeScenario == "pristine") File.WriteAllText(_baselinePath, json);
        File.WriteAllText(Path.Combine(_evidence, "facility-" + _activeScenario + ".json"), json);
        if (_activeScenario == "planned-recipe-noop")
        {
            using var baseline = JsonDocument.Parse(File.ReadAllText(_baselinePath));
            bool matches = baseline.RootElement.GetProperty("RoomsDigest").GetString() == capture.RootElement.GetProperty("RoomsDigest").GetString();
            File.WriteAllText(Path.Combine(_evidence, "recipe-noop-result.txt"), matches
                ? "PASS: combined legacy no-op recipe and whole-layout event exactly match native room descriptors."
                : "FAIL: combined recipe/event layout differs from the native baseline.");
            if (!matches) File.WriteAllText(Path.Combine(_evidence, "failure.txt"), "Combined recipe/event no-op differs from the native baseline.");
        }
    }

    internal static void Configure(MapGeneratingEventArgs ev)
    {
        _activeScenario = _scenario;
        MapEvents.LayoutFinalizing -= FinalizeLayout;
        if (_activeScenario.StartsWith("planned-", StringComparison.Ordinal))
            MapEvents.LayoutFinalizing += FinalizeLayout;
        try { ConfigureCore(ev); }
        catch (Exception exception)
        {
            ev.IsAllowed = false;
            File.WriteAllText(Path.Combine(_evidence, "failure.txt"), "Facility fixture failed: " + exception);
            throw;
        }
    }

    internal static void Shutdown()
    {
        MapEvents.LayoutFinalizing -= FinalizeLayout;
        FacilityFailureProbe.Shutdown();
    }

    private static void ConfigureCore(MapGeneratingEventArgs ev)
    {
        ev.Seed = _seed;
        if (_scenario == "pristine" || (_scenario.StartsWith("planned-", StringComparison.Ordinal) && _scenario != "planned-recipe-noop")) return;

        using var baseline = JsonDocument.Parse(File.ReadAllText(_baselinePath));
        if (baseline.RootElement.GetProperty("Seed").GetInt32() != _seed)
            throw new InvalidOperationException("Facility fixture baseline must use the selected fixed seed.");
        var rooms = baseline.RootElement.GetProperty("Rooms").EnumerateArray().ToArray();
        var applied = new List<object>();
        JsonElement[] Generic(FacilityZone zone) => rooms.Where(room => Int(room, "Zone") == (int)zone &&
            Int(room, "Candidate") == (int)RoomName.Unnamed).OrderBy(room => Int(room, "X")).ThenBy(room => Int(room, "Y")).ToArray();
        void Move(JsonElement room, Vector3 offset)
        {
            FacilityLayout.MoveRoom((FacilityZone)Int(room, "Zone"), Coords(room), offset);
            applied.Add(new { Operation = "move", Zone = Int(room, "Zone"), X = Int(room, "X"), Y = Int(room, "Y"), Offset = new[] { offset.x, offset.y, offset.z } });
        }
        void Rotate(JsonElement room, float yaw)
        {
            FacilityLayout.RotateRoom((FacilityZone)Int(room, "Zone"), Coords(room), yaw);
            applied.Add(new { Operation = "rotate", Zone = Int(room, "Zone"), X = Int(room, "X"), Y = Int(room, "Y"), Yaw = yaw });
        }
        if (_scenario == "noop" || _scenario == "planned-recipe-noop") Move(Generic(FacilityZone.LightContainment).First(), Vector3.zero);
        if (_scenario == "lcz" || _scenario == "hcz")
        {
            FacilityZone zone = _scenario == "lcz" ? FacilityZone.LightContainment : FacilityZone.HeavyContainment;
            var selected = Generic(zone);
            if (selected.Length < 3) throw new InvalidOperationException("Facility fixture requires three generic tiles in " + zone);
            Move(selected[0], new Vector3(1.25f, 0.5f, -2f));
            Rotate(selected[1], 137f);
            FacilityLayout.RemoveRoom(zone, Coords(selected[2]));
            applied.Add(new { Operation = "skip", Zone = (int)zone, X = Int(selected[2], "X"), Y = Int(selected[2], "Y") });
        }
        if (_scenario == "entrance")
        {
            foreach (var zone in new[] { FacilityZone.HeavyContainment, FacilityZone.Entrance })
            {
                var checkpoints = rooms.Where(room => Int(room, "Zone") == (int)zone && Int(room, "Candidate") == (int)RoomName.HczCheckpointToEntranceZone).ToArray();
                if (checkpoints.Length != 2) throw new InvalidOperationException("Facility fixture requires two linking checkpoints in " + zone);
                foreach (var room in checkpoints)
                    // Whole-zone centroid shifts must preserve the native 15m room
                    // lookup grid used by outlying SCP-079 interactables.
                    Move(room, zone == FacilityZone.HeavyContainment ? new Vector3(15f, 0f, 0f) : new Vector3(0f, 0f, 15f));
            }
            var selected = Generic(FacilityZone.Entrance);
            if (selected.Length < 2) throw new InvalidOperationException("Facility fixture requires two generic Entrance tiles.");
            Move(selected[0], new Vector3(-0.5f, 0.25f, 1f));
            Rotate(selected[1], 270f);
        }
        if (_scenario == "atlas")
        {
            var selected = Generic(FacilityZone.LightContainment).Last();
            var generator = UnityEngine.Object.FindObjectsByType<AtlasZoneGenerator>(FindObjectsSortMode.None)
                .Single(value => value.TargetZone == FacilityZone.LightContainment);
            int x = Int(selected, "X") * MapAtlasInterpreter.GlyphSize;
            int y = Int(selected, "Y") * MapAtlasInterpreter.GlyphSize;
            for (int atlas = 0; atlas < generator.Atlases.Length; atlas++)
                for (int dx = 0; dx < MapAtlasInterpreter.GlyphSize; dx++)
                    for (int dy = 0; dy < MapAtlasInterpreter.GlyphSize; dy++)
                    {
                        if (x + dx >= generator.Atlases[atlas].width || y + dy >= generator.Atlases[atlas].height) continue;
                        FacilityLayout.ClearAtlasPixel(FacilityZone.LightContainment, atlas, x + dx, y + dy);
                    }
            applied.Add(new { Operation = "atlas-remove", Zone = (int)FacilityZone.LightContainment, X = Int(selected, "X"), Y = Int(selected, "Y") });
        }
        File.WriteAllText(Path.Combine(_evidence, "recipe-" + _scenario + ".json"), JsonSerializer.Serialize(new { Scenario = _scenario, Seed = _seed, Applied = applied }));
    }

    private static void FinalizeLayout(FacilityLayoutFinalizingEventArgs ev)
    {
        try
        {
            if (ev.Seed != _seed) throw new InvalidOperationException("Finalizing layout has an unexpected seed.");
            var generators = UnityEngine.Object.FindObjectsByType<AtlasZoneGenerator>(FindObjectsSortMode.None)
                .Where(generator => generator.TargetZone == FacilityZone.LightContainment ||
                    generator.TargetZone == FacilityZone.HeavyContainment || generator.TargetZone == FacilityZone.Entrance).ToArray();
            if (generators.Select(generator => generator.TargetZone).Distinct().Count() != 3 || generators.Any(generator => generator.Spawned.Count != 0))
                throw new InvalidOperationException("Whole-layout event must precede room instantiation in all three atlas zones.");
            if (_activeScenario == "planned-recipe-noop" && FacilityLayout.CurrentEntries.Count != 1)
                throw new InvalidOperationException("Combined fixture must carry its earlier no-op recipe into LayoutFinalizing.");
            if (_activeScenario == "planned-failure")
            {
                FacilityFailureProbe.Inject(generators.First(generator => generator.TargetZone == FacilityZone.LightContainment));
                return;
            }
            var applied = new List<object>();
            if (_activeScenario == "planned-move")
            {
                foreach (var zone in new[] { FacilityZone.LightContainment, FacilityZone.HeavyContainment, FacilityZone.Entrance })
                {
                    var rooms = ev.Rooms.Where(room => room.Zone == zone && room.Name == RoomName.Unnamed && !room.IsRemoved)
                        .OrderBy(room => room.Coords.x).ThenBy(room => room.Coords.y).ToArray();
                    if (rooms.Length < 2) throw new InvalidOperationException("Planned fixture needs two generic rooms in " + zone);
                    var moved = rooms[0];
                    var offset = new Vector3(1.25f, 0.5f, -2f);
                    moved.Position += offset;
                    applied.Add(new { Operation = "move", Zone = (int)zone, X = moved.Coords.x, Y = moved.Coords.y,
                        Offset = new[] { offset.x, offset.y, offset.z } });
                    var rotated = rooms[1];
                    rotated.RotationY = 90f;
                    applied.Add(new { Operation = "rotate", Zone = (int)zone, X = rotated.Coords.x, Y = rotated.Coords.y, Yaw = 90f });
                }
            }
            if (_activeScenario == "planned-remove")
            {
                using var baseline = JsonDocument.Parse(File.ReadAllText(_baselinePath));
                if (baseline.RootElement.GetProperty("Seed").GetInt32() != _seed)
                    throw new InvalidOperationException("Duplicate-removal baseline must use the selected fixed seed.");
                // Name/shape alone cannot distinguish multiple prefabs sharing Unnamed.
                // Original network identity ranges identify the actual repeated prefab.
                var repeated = baseline.RootElement.GetProperty("Rooms").EnumerateArray()
                    .Where(room => Int(room, "Candidate") == (int)RoomName.Unnamed && room.GetProperty("Identities").GetArrayLength() > 0)
                    .GroupBy(room => Int(room, "Zone") + "|" + Int(room, "Candidate") + "|" + Int(room, "Shape") + "|" +
                        string.Join(";", room.GetProperty("Identities").EnumerateArray()
                            .Select(identity => identity.GetProperty("Path").GetString() + "=" + identity.GetProperty("OriginalAssetId").GetUInt32())))
                    .Where(group => group.Count() > 1)
                    .OrderBy(group => group.Key, StringComparer.Ordinal).FirstOrDefault();
                if (repeated == null) throw new InvalidOperationException("Fixture needs a repeated room prefab with network identities.");
                var first = repeated.OrderBy(room => Int(room, "DuplicateId")).First();
                var target = ev.Rooms.Single(room => (int)room.Zone == Int(first, "Zone") && room.Coords == Coords(first));
                if ((int)target.Name != Int(first, "Candidate") || (int)target.Shape != Int(first, "Shape") || target.IsRemoved)
                    throw new InvalidOperationException("Planned duplicate-removal target differs from the native baseline.");
                target.IsRemoved = true;
                applied.Add(new { Operation = "skip", Zone = (int)target.Zone, X = target.Coords.x, Y = target.Coords.y,
                    RemovedDuplicateId = Int(first, "DuplicateId"), RepeatedPrefabCount = repeated.Count() });
            }
            if (_activeScenario == "planned-checkpoint")
            {
                var target = ev.Rooms.Where(room => room.Zone == FacilityZone.HeavyContainment &&
                        room.Name == RoomName.HczCheckpointToEntranceZone && !room.IsRemoved)
                    .OrderBy(room => room.Coords.x).ThenBy(room => room.Coords.y).First();
                target.IsRemoved = true;
                applied.Add(new { Operation = "skip", Zone = (int)target.Zone, X = target.Coords.x, Y = target.Coords.y });
            }
            string json = JsonSerializer.Serialize(new
            {
                Scenario = _activeScenario, Seed = ev.Seed, ev.GenerationId, WholeLayoutEvent = true,
                PlannedRooms = ev.Rooms.Count, MaterializedRooms = 0, LegacyRecipeEntries = FacilityLayout.CurrentEntries.Count, Applied = applied
            });
            File.WriteAllText(Path.Combine(_evidence, "recipe-" + _activeScenario + ".json"), json);
            File.WriteAllText(Path.Combine(_evidence, "recipe-generation-" + ev.GenerationId + ".json"), json);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(_evidence, "failure.txt"), "Facility finalizing fixture failed: " + exception);
            throw;
        }
    }

    private static int Int(JsonElement item, string key) => item.GetProperty(key).GetInt32();
    private static Vector2Int Coords(JsonElement room) => new Vector2Int(Int(room, "X"), Int(room, "Y"));
}
