using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Anomaly.Server.Api.Events.Handlers;
using Anomaly.Server.Api.Events.Arguments;
using Anomaly.Server.Api.Map;
using Anomaly.Shared.Networking.Messages;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using MapGeneration;
using UnityEngine;

namespace Anomaly.Server.Internal.Map;

internal static class FacilityLayoutRuntime
{
    private static readonly Dictionary<(byte, short, short), FacilityRecipeEntry> Entries = new();
    private static readonly Dictionary<(byte, byte, ushort, ushort), AtlasPixelEdit> Pixels = new();
    private static FacilitySnapshotMessage _snapshot;
    private static ulong _generationId;
    private static bool _authoring;
    private static bool _initialized;
    private static bool _prepared;
    private static bool _published;
    internal static event Action<FacilitySnapshotMessage> SnapshotFrozen;
    internal static event Action<Exception> GenerationRejected;
    internal static bool GenerationFailed { get; private set; }
    internal static FacilitySnapshotMessage CurrentSnapshot => _snapshot;
    internal static IReadOnlyCollection<(FacilityZone Zone, Vector2Int Coords)> CurrentEntries =>
        Entries.Values.Select(e => ((FacilityZone)e.ZoneId, new Vector2Int(e.CoordsX, e.CoordsY))).ToArray();

    internal static void Init()
    {
        if (_initialized) return;
        _initialized = true;
        ServerEvents.MapGenerated += OnMapGenerated;
    }

    internal static void Disable()
    {
        if (_initialized) ServerEvents.MapGenerated -= OnMapGenerated;
        _initialized = false;
        _authoring = false;
        _prepared = _published = false;
        Entries.Clear(); Pixels.Clear(); _snapshot = null;
    }

    internal static void BeginAuthoring()
    {
        FacilityGenerationDiagnostics.Clear();
        GenerationFailed = false;
        _prepared = _published = false;
        Entries.Clear(); Pixels.Clear(); _snapshot = null;
        _authoring = true;
    }

    internal static void Freeze()
    {
        if (!_authoring) return;
        _authoring = false;
        if (_generationId == ulong.MaxValue) throw new InvalidOperationException("Facility generation identifier exhausted.");
        var snapshot = new FacilitySnapshotMessage
        {
            GenerationId = ++_generationId, Seed = SeedSynchronizer.Seed,
            Recipe = Entries.Values.ToList(), AtlasEdits = Pixels.Values.ToList()
        };
        snapshot.Mode = snapshot.Seed != -1 && (snapshot.HasMutations || MapEvents.HasLayoutFinalizingSubscribers)
            ? FacilityGenerationMode.Planned : FacilityGenerationMode.Native;
        FacilityGeneration.ValidateAssets(snapshot);
        _snapshot = snapshot;
        if (snapshot.Mode == FacilityGenerationMode.Native) snapshot.Seal();
    }

    internal static void PreparePlan(ZoneGenerator[] generators, System.Random rng)
    {
        if (_prepared || _snapshot?.Mode != FacilityGenerationMode.Planned) return;
        _prepared = true;
        foreach (var generator in generators)
            if (generator is AtlasZoneGenerator atlas)
                _snapshot.Zones.Add(FacilityGeneration.Plan(atlas, rng, _snapshot));

        var placements = new List<FacilityRoomPlacement>();
        var records = new List<FacilityPlannedRoom>();
        int thread = Thread.CurrentThread.ManagedThreadId;
        foreach (var zone in _snapshot.Zones)
            foreach (var room in zone.Rooms)
            {
                if (!room.HasPlacement) continue;
                placements.Add(new FacilityRoomPlacement((FacilityZone)zone.ZoneId, new Vector2Int(room.CoordsX, room.CoordsY),
                    (RoomName)room.Name, (RoomShape)room.Shape, new Vector3(room.PositionX, room.PositionY, room.PositionZ), room.RotationY, thread));
                records.Add(room);
            }
        try { MapEvents.OnLayoutFinalizing(new FacilityLayoutFinalizingEventArgs(_snapshot.GenerationId, _snapshot.Seed, placements)); }
        finally { foreach (var placement in placements) placement.Seal(); }
        for (int i = 0; i < records.Count; i++)
        {
            var placement = placements[i];
            var room = records[i];
            room.PositionX = placement.Position.x; room.PositionY = placement.Position.y; room.PositionZ = placement.Position.z;
            room.RotationY = placement.RotationY; room.IsRemoved = placement.IsRemoved;
        }
        _snapshot.Seal();
        FacilityGeneration.ValidateAssets(_snapshot);
    }

    internal static void ThrowIfFailed()
    {
        if (GenerationFailed) throw new InvalidOperationException("Facility generation failed before map registration.");
        if (_snapshot == null || (_snapshot.Mode == FacilityGenerationMode.Planned && !_prepared))
            throw new InvalidOperationException("Facility generation did not prepare its placement plan.");
    }

    internal static void Publish()
    {
        ThrowIfFailed();
        if (!SeedSynchronizer.MapGenerated || _published) return;
        _snapshot.RoomsDigest = FacilityGenerationDiagnostics.ComputeRoomsDigest();
        _snapshot.Seal();
        _published = true;
        SnapshotFrozen?.Invoke(_snapshot.Copy());
        LabApi.Features.Console.Logger.Info($"Facility snapshot published: generation={_snapshot.GenerationId}, seed={_snapshot.Seed}, mode={_snapshot.Mode}, digest={Convert.ToBase64String(_snapshot.Digest)}.");
    }

    internal static bool TryGetSnapshot(out FacilitySnapshotMessage snapshot)
    {
        snapshot = _published ? _snapshot?.Copy() : null;
        return snapshot != null;
    }

    internal static void FailGeneration(Exception exception)
    {
        if (GenerationFailed) return;
        GenerationFailed = true;
        _published = false;
        _snapshot = null;
        foreach (Delegate subscriber in GenerationRejected?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { ((Action<Exception>)subscriber)(exception); }
            catch (Exception publicationFailure)
            {
                try { LabApi.Features.Console.Logger.Error("Facility rejection handler failed: " + publicationFailure); }
                catch { }
            }
        }
        try { LabApi.Features.Console.Logger.Error("Facility generation rejected: " + exception); }
        catch { }
    }

    internal static FacilityRecipeEntry GetForEdit(FacilityZone zone, Vector2Int coords)
    {
        RequireAuthoring();
        ValidateZone(zone);
        if (coords.x < short.MinValue || coords.x > short.MaxValue || coords.y < short.MinValue || coords.y > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(coords));
        if (Entries.TryGetValue(((byte)zone, (short)coords.x, (short)coords.y), out var entry)) return entry;
        if (Entries.Count >= FacilitySnapshotMessage.MaximumEntries) throw new InvalidOperationException("Facility room limit reached.");
        return new FacilityRecipeEntry { ZoneId = (byte)zone, CoordsX = (short)coords.x, CoordsY = (short)coords.y };
    }

    internal static void Set(FacilityRecipeEntry entry)
    {
        RequireAuthoring();
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
            new FacilityRecipeCodec { Entries = new List<FacilityRecipeEntry> { entry } }.Serialize(writer);
        Entries[(entry.ZoneId, entry.CoordsX, entry.CoordsY)] = entry;
    }

    internal static void SetPixel(FacilityZone zone, int atlas, int x, int y, Color32 color)
    {
        RequireAuthoring();
        ValidateZone(zone);
        if (atlas < 0 || atlas > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(atlas));
        if (x < 0 || x > ushort.MaxValue || y < 0 || y > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(x));
        var key = ((byte)zone, (byte)atlas, (ushort)x, (ushort)y);
        if (!Pixels.ContainsKey(key) && Pixels.Count >= FacilitySnapshotMessage.MaximumAtlasEdits)
            throw new InvalidOperationException("Facility atlas edit limit reached.");
        Pixels[key] = new AtlasPixelEdit { ZoneId = (byte)zone, AtlasIdx = (byte)atlas, PixelX = (ushort)x, PixelY = (ushort)y, R = color.r, G = color.g, B = color.b };
    }

    internal static void ClearStaged() { RequireAuthoring(); Entries.Clear(); Pixels.Clear(); }
    internal static void ClearAtlasEdits() { RequireAuthoring(); Pixels.Clear(); }
    private static void ValidateZone(FacilityZone zone)
    {
        if (zone != FacilityZone.LightContainment && zone != FacilityZone.HeavyContainment && zone != FacilityZone.Entrance)
            throw new ArgumentOutOfRangeException(nameof(zone), "Only LightContainment, HeavyContainment and Entrance support atlas mutations.");
    }
    private static void RequireAuthoring()
    {
        if (!_authoring) throw new InvalidOperationException("Facility mutations are allowed only during ServerEvents.MapGenerating, before the snapshot freezes.");
    }
    private static void OnMapGenerated(MapGeneratedEventArgs _)
    {
        if (!GenerationFailed && _snapshot != null)
        {
            try { MapEvents.OnRecipeApplied(); }
            catch (Exception exception)
            {
                try { LabApi.Features.Console.Logger.Error("Facility recipe event publication failed: " + exception); }
                catch { }
            }
        }
    }
}
