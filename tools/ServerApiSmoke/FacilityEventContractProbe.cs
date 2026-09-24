using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Anomaly.Server.Api.Events.Arguments;
using Anomaly.Server.Api.Events.Handlers;
using Anomaly.Server.Api.Map;
using MapGeneration;
using UnityEngine;

namespace Anomaly.ServerApiSmoke;

/// <summary>Environment-only live checks for the synchronous layout-editing contract.</summary>
internal static class FacilityEventContractProbe
{
    private static readonly List<Assertion> Assertions = new List<Assertion>();
    private static readonly List<string> Order = new List<string>();
    private static string _evidence;
    private static bool _enabled;
    private static bool _captured;
    private static float _deadline;
    private static FacilityRoomPlacement _retained;
    private static Vector3 _originalPosition;
    private static float _originalRotation;
    private static Vector3 _firstPosition;
    private static float _firstRotation;
    private static ulong _generation;
    private static int _seed;
    private static int _eventCount;

    internal static void Initialize(string evidence)
    {
        _enabled = Environment.GetEnvironmentVariable("ANOMALY_SERVER_SMOKE_EVENT_CONTRACT") == "1";
        if (!_enabled) return;
        _evidence = evidence;
        _captured = false;
        _retained = null;
        _eventCount = 0;
        Assertions.Clear();
        Order.Clear();
        _deadline = Time.realtimeSinceStartup + 90f;
        MapEvents.LayoutFinalizing += FirstHandler;
        MapEvents.LayoutFinalizing += ThrowingHandler;
        MapEvents.LayoutFinalizing += LastHandler;
        StaticUnityMethods.OnUpdate += Tick;
    }

    internal static void Shutdown()
    {
        MapEvents.LayoutFinalizing -= FirstHandler;
        MapEvents.LayoutFinalizing -= ThrowingHandler;
        MapEvents.LayoutFinalizing -= LastHandler;
        StaticUnityMethods.OnUpdate -= Tick;
        _enabled = false;
        _retained = null;
    }

    private static void FirstHandler(FacilityLayoutFinalizingEventArgs ev)
    {
        Order.Add("first");
        _eventCount++;
        _generation = ev.GenerationId;
        _seed = ev.Seed;
        _deadline = Time.realtimeSinceStartup + 1f;
        _retained = ev.Rooms.First(room => room.Name == RoomName.Unnamed && !room.IsRemoved);
        _originalPosition = _retained.Position;
        _originalRotation = _retained.RotationY;
        _retained.Position = _originalPosition + new Vector3(0.25f, 0f, 0f);
        _retained.RotationY = _originalRotation + 90f;
        _retained.IsRemoved = true;
        _firstPosition = _retained.Position;
        _firstRotation = _retained.RotationY;
        Require("First handler can edit position, rotation and removal", _firstPosition != _originalPosition && _retained.IsRemoved);
    }

    private static void ThrowingHandler(FacilityLayoutFinalizingEventArgs _)
    {
        Order.Add("throwing");
        throw new InvalidOperationException("EXPECTED isolated facility event-contract probe handler failure.");
    }

    private static void LastHandler(FacilityLayoutFinalizingEventArgs ev)
    {
        Order.Add("last");
        Require("Dispatch continues after a throwing handler", _retained != null && _eventCount == 1);
        if (_retained == null) return;
        Require("Later handler sees earlier descriptor edits", ev.Rooms.Contains(_retained) &&
            _retained.Position == _firstPosition && _retained.RotationY == _firstRotation && _retained.IsRemoved);
        Exception invalidPosition = Observe(() => _retained.Position = new Vector3(float.NaN, 0f, 0f));
        Require("Invalid position rejects and preserves the valid value", invalidPosition is ArgumentOutOfRangeException && _retained.Position == _firstPosition);
        Exception invalidRotation = Observe(() => _retained.RotationY = float.PositiveInfinity);
        Require("Invalid rotation rejects and preserves the valid value", invalidRotation is ArgumentOutOfRangeException && _retained.RotationY == _firstRotation);

        Exception offThread = null;
        Vector3 attemptedPosition = _originalPosition;
        var worker = new Thread(() => offThread = Observe(() => _retained.Position = attemptedPosition)) { IsBackground = true };
        worker.Start();
        bool joined = worker.Join(5000);
        Require("Off-thread descriptor edits reject without changing the value", joined && offThread is InvalidOperationException && _retained.Position == _firstPosition);

        _retained.Position = _originalPosition;
        _retained.RotationY = _originalRotation;
        _retained.IsRemoved = false;
        Require("Later handler assignment wins for every editable field", _retained.Position == _originalPosition &&
            Mathf.Abs(Mathf.DeltaAngle(_retained.RotationY, _originalRotation)) < 0.001f && !_retained.IsRemoved);
    }

    private static void Tick()
    {
        if (!_enabled || _captured || Time.realtimeSinceStartup < _deadline) return;
        _captured = true;
        StaticUnityMethods.OnUpdate -= Tick;
        string error = null;
        try
        {
            Require("Handlers ran once in subscription order", _eventCount == 1 && Order.SequenceEqual(new[] { "first", "throwing", "last" }));
            Require("A descriptor was retained beyond the callback", _retained != null);
            if (_retained != null)
            {
                Require("Retained position setter rejects after freeze", Observe(() => _retained.Position = _originalPosition) is InvalidOperationException);
                Require("Retained rotation setter rejects after freeze", Observe(() => _retained.RotationY = _originalRotation) is InvalidOperationException);
                Require("Retained removal setter rejects after freeze", Observe(() => _retained.IsRemoved = true) is InvalidOperationException);
                Require("Rejected retained edits preserve the final descriptor", _retained.Position == _originalPosition &&
                    Mathf.Abs(Mathf.DeltaAngle(_retained.RotationY, _originalRotation)) < 0.001f && !_retained.IsRemoved);
                var generator = UnityEngine.Object.FindObjectsByType<AtlasZoneGenerator>(FindObjectsSortMode.None)
                    .Single(value => value.TargetZone == _retained.Zone);
                var rooms = generator.Spawned.Where(room => room.Interpretation.Coords == _retained.Coords).ToArray();
                Require("Final winning placement was materialized", rooms.Length == 1 && rooms[0].Instance.transform.position == _originalPosition &&
                    Mathf.Abs(Mathf.DeltaAngle(rooms[0].Instance.transform.eulerAngles.y, _originalRotation)) < 0.001f);
            }
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            Type runtime = typeof(Anomaly.Server.Plugin).Assembly.GetType("Anomaly.Server.Internal.Map.FacilityLayoutRuntime", true);
            bool generationFailed = (bool)runtime.GetProperty("GenerationFailed", flags).GetValue(null);
            var snapshotArguments = new object[] { null };
            bool snapshotAvailable = (bool)runtime.GetMethod("TryGetSnapshot", flags).Invoke(null, snapshotArguments);
            Require("Expected handler exception does not reject generation", !generationFailed && SeedSynchronizer.MapGenerated);
            Require("Completed generation publishes its snapshot", snapshotAvailable && snapshotArguments[0] != null);
        }
        catch (Exception exception)
        {
            error = exception.ToString();
            Require("Probe inspection completed without error", false);
        }
        bool passed = Assertions.Count > 0 && Assertions.All(assertion => assertion.Passed);
        File.WriteAllText(Path.Combine(_evidence, "event-contract-probe.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, GenerationId = _generation, Seed = _seed, HandlerOrder = Order,
            Assertions, Error = error
        }));
        File.WriteAllText(Path.Combine(_evidence, "event-contract-result.txt"), passed
            ? "PASS: handler ordering, exception isolation, value validation and descriptor edit lifetime verified live."
            : "FAIL: inspect event-contract-probe.json.");
        if (!passed) File.WriteAllText(Path.Combine(_evidence, "failure.txt"), "Facility event-contract probe failed: " + error);
    }

    private static Exception Observe(Action action)
    {
        try { action(); return null; }
        catch (Exception exception) { return exception; }
    }

    private static void Require(string name, bool condition) => Assertions.Add(new Assertion { Name = name, Passed = condition });

    private sealed class Assertion
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
    }
}
