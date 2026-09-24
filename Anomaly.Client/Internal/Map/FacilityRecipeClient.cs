using System;
using System.Diagnostics;
using Anomaly.Client.Auth;
using Anomaly.Client.Api.Networking;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Il2CppMapGeneration;
using Il2CppMirror;
using MelonLoader;

namespace Anomaly.Client.Internal.Map;

/// <summary>Accepts only authenticated snapshots and holds generation/readiness until the matching seed exists.</summary>
internal static class FacilityRecipeClient
{
    private static readonly FacilityGenerationGate Gate = new();
    private static bool _sceneAvailable;
    private static bool _failed;
    private static bool _readyPending;
    private static bool _playerPending;
    internal static FacilitySnapshotMessage CurrentSnapshot => Gate.Snapshot;
    internal static bool IsGenerating => Gate.Generating && !_failed;
    internal static bool IsReady => Gate.Completed && !_failed && _sceneAvailable && SeedSynchronizer.MapGenerated;

    internal static void RegisterHandlers()
    {
        Clear();
        AnomalyMessaging.FacilitySnapshotReceived = ReceiveSnapshot;
    }

    internal static void BeginConnection()
    {
        if (AuthHandler.UsingAnomalyAuth && !NetworkServer.active) Gate.BeginWaiting(Now);
    }

    internal static void ReceiveSnapshot(FacilitySnapshotMessage snapshot)
    {
        try
        {
            Gate.ReceiveSnapshot(snapshot);
            if (!Gate.Completed) Gate.BeginWaiting(Now);
            TryGenerate();
        }
        catch (Exception exception) { FailGeneration(exception); }
    }

    internal static void ReceiveSeed(int seed)
    {
        try
        {
            Gate.ReceiveSeed(seed);
            if (!Gate.Completed) Gate.BeginWaiting(Now);
            TryGenerate();
        }
        catch (Exception exception) { FailGeneration(exception); }
    }

    internal static void SceneAvailable()
    {
        _sceneAvailable = true;
        Gate.BeginWaiting(Now);
        TryGenerate();
    }

    internal static void SceneDestroyed()
    {
        _sceneAvailable = false;
        FacilityStructureRotation.Clear();
        Gate.EndScene();
        Gate.BeginWaiting(Now);
    }

    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    internal static void Tick()
    {
        if (!AuthHandler.UsingAnomalyAuth || _failed || IsReady) return;
        if (Gate.HasTimedOut(Now)) FailGeneration(new TimeoutException("Authenticated facility bootstrap was not available within 30 seconds."));
    }

    internal static bool DeferReady()
    {
        if (IsReady) return false;
        _readyPending = true;
        if (_sceneAvailable) Gate.BeginWaiting(Now);
        return true;
    }

    internal static bool DeferPlayer()
    {
        if (IsReady) return false;
        _playerPending = true;
        if (_sceneAvailable) Gate.BeginWaiting(Now);
        return true;
    }

    private static void TryGenerate()
    {
        if (_failed || !_sceneAvailable || SeedSynchronizer._singleton == null || SeedSynchronizer.MapGenerated || !Gate.TryBegin()) return;
        try
        {
            FacilityGeneration.ValidateAssets(Gate.Snapshot);
            // The wrapper call travels managed -> native with a blittable four-byte seed.
            SeedSynchronizer.ClientReceiveSeed(new SeedSynchronizer.SeedMessage { Value = Gate.Snapshot.Seed });
            if (!SeedSynchronizer.MapGenerated || _failed)
                throw new InvalidOperationException("Authenticated facility generation did not complete.");
            if (!FacilityGeneration.EqualDigest(Gate.Snapshot.RoomsDigest, FacilityGenerationDiagnostics.ComputeRoomsDigest()))
                throw new InvalidOperationException("Generated facility room/child identity digest differs from the server.");
            Gate.Complete();
            MelonLogger.Msg($"Facility bootstrap accepted: generation={Gate.Snapshot.GenerationId}, seed={Gate.Snapshot.Seed}, digest={Convert.ToBase64String(Gate.Snapshot.Digest)}.");
            if (_readyPending) { _readyPending = false; NetworkClient.Ready(); }
            if (_playerPending) { _playerPending = false; NetworkClient.AddPlayer(); }
        }
        catch (Exception exception) { FailGeneration(exception); }
    }

    internal static void FailGeneration(Exception exception)
    {
        _failed = true;
        _readyPending = _playerPending = false;
        AnomalyMessaging.FailClosedTransport("Facility bootstrap/generation failed: " + exception.Message);
        MelonLogger.Error("Facility generation rejected: " + exception);
    }

    internal static void Clear()
    {
        Gate.Reset();
        FacilityGenerationDiagnostics.Clear();
        FacilityStructureRotation.Clear();
        _sceneAvailable = false;
        _failed = _readyPending = _playerPending = false;
    }
}
