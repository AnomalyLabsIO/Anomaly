using System;
using System.Collections.Generic;
using Anomaly.Server.Internal.Map;
using Anomaly.Shared.Networking.Messages;
using MapGeneration;
using UnityEngine;

namespace Anomaly.Server.Api.Map;

/// <summary>Stages deterministic facility changes during LabAPI ServerEvents.MapGenerating.</summary>
/// <remarks>
/// Call on the server thread. The recipe freezes after all MapGenerating handlers return.
/// Authors must preserve valid native room topology. In particular, whole-zone checkpoint
/// offsets should preserve the 15-metre horizontal room grid used by SCP-079 interactables.
/// A native generation failure rejects the generation and its client connections.
/// </remarks>
public static class FacilityLayout
{
    public static IReadOnlyCollection<(FacilityZone Zone, Vector2Int Coords)> CurrentEntries => FacilityLayoutRuntime.CurrentEntries;

    /// <summary>Adds a world-space displacement to the tile's vanilla placement.</summary>
    public static void MoveRoom(FacilityZone zone, Vector2Int coords, Vector3 offset)
    {
        var entry = FacilityLayoutRuntime.GetForEdit(zone, coords);
        entry.OpType = FacilityRecipeCodec.OpTransformOverride;
        entry.DirtyMask |= FacilityRecipeCodec.DirtyPosition;
        entry.OffsetX = offset.x; entry.OffsetY = offset.y; entry.OffsetZ = offset.z;
        FacilityLayoutRuntime.Set(entry);
    }

    /// <summary>Sets the absolute world-space Y rotation, including in Entrance.</summary>
    public static void RotateRoom(FacilityZone zone, Vector2Int coords, float yRotation)
    {
        var entry = FacilityLayoutRuntime.GetForEdit(zone, coords);
        entry.OpType = FacilityRecipeCodec.OpTransformOverride;
        entry.DirtyMask |= FacilityRecipeCodec.DirtyRotation;
        entry.RotationY = yRotation;
        FacilityLayoutRuntime.Set(entry);
    }

    public static void RemoveRoom(FacilityZone zone, Vector2Int coords)
    {
        var entry = FacilityLayoutRuntime.GetForEdit(zone, coords);
        entry.OpType = FacilityRecipeCodec.OpSkip;
        entry.DirtyMask = 0;
        entry.OffsetX = entry.OffsetY = entry.OffsetZ = entry.RotationY = 0;
        FacilityLayoutRuntime.Set(entry);
    }

    public static void ClearRecipe() => FacilityLayoutRuntime.ClearStaged();
    public static void ClearAtlasEdits() => FacilityLayoutRuntime.ClearAtlasEdits();
    public static void SetAtlasPixel(FacilityZone zone, int atlasIdx, int pixelX, int pixelY, Color32 color) =>
        FacilityLayoutRuntime.SetPixel(zone, atlasIdx, pixelX, pixelY, color);

    public static void SetAtlasPixel(FacilityZone zone, int atlasIdx, int pixelX, int pixelY, RoomShape shape)
    {
        if (!FacilityGlyphs.TryGetColor(shape, out var color)) throw new InvalidOperationException("No facility glyph exists for this shape.");
        SetAtlasPixel(zone, atlasIdx, pixelX, pixelY, color);
    }

    public static void SetAtlasPixel(FacilityZone zone, int atlasIdx, int pixelX, int pixelY, RoomName room)
    {
        if (!FacilityGlyphs.TryGetColor(room, out var color)) throw new InvalidOperationException("No specific facility glyph exists for this room.");
        SetAtlasPixel(zone, atlasIdx, pixelX, pixelY, color);
    }

    public static void ClearAtlasPixel(FacilityZone zone, int atlasIdx, int pixelX, int pixelY) =>
        SetAtlasPixel(zone, atlasIdx, pixelX, pixelY, FacilityGlyphs.Empty);
}
