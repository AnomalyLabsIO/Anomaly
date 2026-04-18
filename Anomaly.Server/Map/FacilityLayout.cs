using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Transport.Messages;
using LabApi.Events.Handlers;
using MapGeneration;
using Mirror;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Map
{
    /// <summary>
    /// Public plugin surface for describing pre-generation facility mutations.
    /// Plugins call <see cref="MoveRoom"/>, <see cref="RotateRoom"/>, and
    /// <see cref="RemoveRoom"/> during <c>ServerEvents.MapGenerating</c> to
    /// stage deltas that both the server and every client apply deterministically
    /// while running <c>AtlasZoneGenerator</c>. The recipe is shipped per-client
    /// in the same early Mirror connection slot vanilla uses for
    /// <c>SeedMessage</c>, so it arrives before the client's deterministic
    /// <c>GenerateLevel</c> pass without depending on hub/auth readiness.
    /// </summary>
    public static class FacilityLayout
    {
        private static readonly ushort RecipeMessageId = GetMessageId(typeof(FacilityRecipeBootstrapMessage));
        private static readonly ushort AtlasEditsMessageId = GetMessageId(typeof(FacilityAtlasEditsBootstrapMessage));

        private static readonly Dictionary<(FacilityZone zone, Vector2Int coords), RecipeEntry> _entries =
            new Dictionary<(FacilityZone, Vector2Int), RecipeEntry>();

        private static readonly List<AtlasPixelEdit> _atlasEdits = new List<AtlasPixelEdit>();

        private static bool _initialized;

        /// <summary>
        /// Fires after the server has finished applying the recipe to its own
        /// generated facility. Mostly useful for debug logging.
        /// </summary>
        public static event Action RecipeApplied;

        /// <summary>
        /// Wire up lifecycle hooks. Called once from <c>Plugin.Enable</c>.
        /// Harmony patches are applied by the surrounding <c>Harmony.PatchAll</c>.
        /// </summary>
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            ServerEvents.RoundRestarted  += OnRoundRestarted;
            ServerEvents.MapGenerated    += OnMapGenerated;

            Logger.Info("FacilityLayout: initialized.");
        }

        // ---- Public API -------------------------------------------------

        /// <summary>
        /// Displace the room the atlas generator would place at
        /// <paramref name="coords"/> by <paramref name="offset"/> (world
        /// space). The underlying rotation and candidate selection are
        /// unchanged.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if called after
        /// the facility has already been generated this round.</exception>
        public static void MoveRoom(FacilityZone zone, Vector2Int coords, Vector3 offset)
        {
            RequirePreGeneration();
            var key = (zone, coords);
            _entries.TryGetValue(key, out var entry);
            entry.Zone       = zone;
            entry.Coords     = coords;
            entry.OpType     = OpType.TransformOverride;
            entry.DirtyMask |= FacilityLayoutRecipeMessage.DirtyPosition;
            entry.Offset     = offset;
            _entries[key]    = entry;
        }

        /// <summary>
        /// Override the spawned room's Y-axis rotation. Value is absolute
        /// (replaces the atlas-assigned rotation + any zone-specific offset).
        /// </summary>
        public static void RotateRoom(FacilityZone zone, Vector2Int coords, float yRotation)
        {
            RequirePreGeneration();
            var key = (zone, coords);
            _entries.TryGetValue(key, out var entry);
            entry.Zone       = zone;
            entry.Coords     = coords;
            entry.OpType     = OpType.TransformOverride;
            entry.DirtyMask |= FacilityLayoutRecipeMessage.DirtyRotation;
            entry.RotationY  = yRotation;
            _entries[key]    = entry;
        }

        /// <summary>
        /// Suppress the room the atlas would place at <paramref name="coords"/>.
        /// The atlas interpretation is still iterated, but <c>ProcessInterpreted</c>
        /// exits early so no prefab is instantiated. Neighbouring rooms still
        /// see the empty tile when computing adjacency weights.
        /// </summary>
        public static void RemoveRoom(FacilityZone zone, Vector2Int coords)
        {
            RequirePreGeneration();
            var key = (zone, coords);
            _entries[key] = new RecipeEntry
            {
                Zone   = zone,
                Coords = coords,
                OpType = OpType.Skip
            };
        }

        /// <summary>Discard every staged mutation.</summary>
        public static void ClearRecipe()
        {
            _entries.Clear();
            _atlasEdits.Clear();
        }

        // ---- Atlas pixel editing ----------------------------------------

        /// <summary>
        /// Stage a single pixel edit on one atlas texture used by
        /// <c>AtlasZoneGenerator</c>. The edit is applied to a clone of the
        /// original atlas right before <c>MapAtlasInterpreter.Interpret</c>
        /// runs, so the vanilla asset is never mutated. Because both server
        /// and client receive the same edits, the resulting interpreted
        /// layout is byte-identical.
        /// </summary>
        /// <param name="zone">The target zone's generator.</param>
        /// <param name="atlasIdx">Index into the generator's <c>Atlases</c>
        /// array. If the RNG picks a different atlas this round the edit
        /// won't apply — to force consistency, populate all indices or
        /// design atlases with a single entry.</param>
        /// <param name="pixelX">Pixel X coordinate in the atlas (0 = left).</param>
        /// <param name="pixelY">Pixel Y coordinate in the atlas (0 = bottom).</param>
        /// <param name="color">RGB color. Alpha is ignored by the game's
        /// pixel matcher; use a valid <c>GlyphShapePair.GlyphColor</c> to
        /// produce a room, or any other color to leave the tile empty.</param>
        public static void SetAtlasPixel(FacilityZone zone, int atlasIdx, int pixelX, int pixelY, Color32 color)
        {
            RequirePreGeneration();
            if (atlasIdx < 0 || atlasIdx > 255)
                throw new ArgumentOutOfRangeException(nameof(atlasIdx), "AtlasIdx must fit in a byte (0-255).");
            if (pixelX < 0 || pixelX > ushort.MaxValue || pixelY < 0 || pixelY > ushort.MaxValue)
                throw new ArgumentOutOfRangeException("pixelX/pixelY", "Pixel coordinates must fit in ushort (0-65535).");

            _atlasEdits.Add(new AtlasPixelEdit
            {
                ZoneId   = (byte)zone,
                AtlasIdx = (byte)atlasIdx,
                PixelX   = (ushort)pixelX,
                PixelY   = (ushort)pixelY,
                R        = color.r,
                G        = color.g,
                B        = color.b
            });
        }

        /// <summary>
        /// Convenience overload: resolves <paramref name="shape"/> to its
        /// <see cref="Color32"/> via <see cref="FacilityGlyphs.TryGetColor(RoomShape, out Color32)"/>
        /// and writes that color to <c>(pixelX, pixelY)</c>. Throws if the
        /// glyph palette hasn't been registered yet (pre-facility-scene) or
        /// if the shape isn't in the palette.
        /// </summary>
        public static void SetAtlasPixel(FacilityZone zone, int atlasIdx, int pixelX, int pixelY, RoomShape shape)
        {
            if (!FacilityGlyphs.TryGetColor(shape, out var color))
                throw new InvalidOperationException(
                    $"FacilityGlyphs: no color registered for shape {shape}. Call during/after ServerEvents.MapGenerating.");
            SetAtlasPixel(zone, atlasIdx, pixelX, pixelY, color);
        }

        /// <summary>
        /// Convenience overload: resolves <paramref name="room"/> to its
        /// <see cref="Color32"/> via the glyph palette's <c>SpecificRooms</c>
        /// lists and writes that color. Only works for rooms that are actually
        /// registered as "specific" on some pair — generic rooms fall back to
        /// shape-based matching and don't have a dedicated color.
        /// </summary>
        public static void SetAtlasPixel(FacilityZone zone, int atlasIdx, int pixelX, int pixelY, RoomName room)
        {
            if (!FacilityGlyphs.TryGetColor(room, out var color))
                throw new InvalidOperationException(
                    $"FacilityGlyphs: no color registered for specific room {room}. " +
                    $"Not every RoomName is pinned to a glyph; use the RoomShape overload for generic rooms.");
            SetAtlasPixel(zone, atlasIdx, pixelX, pixelY, color);
        }

        /// <summary>
        /// Set a pixel to <see cref="FacilityGlyphs.Empty"/>, which the atlas
        /// interpreter skips silently — the tile ends up empty and no room
        /// spawns there.
        /// </summary>
        public static void ClearAtlasPixel(FacilityZone zone, int atlasIdx, int pixelX, int pixelY)
        {
            SetAtlasPixel(zone, atlasIdx, pixelX, pixelY, FacilityGlyphs.Empty);
        }

        /// <summary>Discard every staged atlas pixel edit (leaves room ops intact).</summary>
        public static void ClearAtlasEdits()
        {
            _atlasEdits.Clear();
        }

        /// <summary>
        /// Consumed by the Harmony patch: pixel edits targeting
        /// <paramref name="zone"/>+<paramref name="atlasIdx"/>, in staging order.
        /// Returns an empty list if none are staged.
        /// </summary>
        internal static List<AtlasPixelEdit> GetAtlasEdits(FacilityZone zone, int atlasIdx)
        {
            var list = new List<AtlasPixelEdit>();
            byte z = (byte)zone;
            byte a = (byte)atlasIdx;
            foreach (var e in _atlasEdits)
                if (e.ZoneId == z && e.AtlasIdx == a) list.Add(e);
            return list;
        }

        /// <summary>
        /// Consumed by the Harmony patches: the current mutation for
        /// <paramref name="zone"/>+<paramref name="coords"/>, if any.
        /// </summary>
        internal static bool TryGet(FacilityZone zone, Vector2Int coords, out RecipeEntry entry)
            => _entries.TryGetValue((zone, coords), out entry);

        /// <summary>
        /// Read-only view of the active recipe. Empty when no plugin has
        /// staged mutations this round.
        /// </summary>
        public static IReadOnlyCollection<(FacilityZone Zone, Vector2Int Coords)> CurrentEntries
        {
            get
            {
                var list = new List<(FacilityZone, Vector2Int)>(_entries.Count);
                foreach (var k in _entries.Keys) list.Add(k);
                return new ReadOnlyCollection<(FacilityZone, Vector2Int)>(list);
            }
        }

        internal static void InvokeRecipeApplied()
        {
            try { RecipeApplied?.Invoke(); }
            catch (Exception ex) { Logger.Error($"FacilityLayout.RecipeApplied handler threw: {ex}"); }
        }

        // ---- Per-connection bootstrap send ------------------------------

        /// <summary>
         /// Called from the <c>SeedSynchronizer.OnNewPlayerConnected</c> prefix
        /// when vanilla would send <c>SeedMessage</c>. We send the facility
        /// bootstrap as plain Mirror messages first so the client can apply it
        /// before <c>ClientReceiveSeed</c> starts local generation.
        /// </summary>
        internal static void SendBootstrap(NetworkConnectionToClient conn)
        {
            if (conn == null) return;

            try
            {
                SendBootstrapPayload(conn, RecipeMessageId, BuildRecipePayload());
                SendBootstrapPayload(conn, AtlasEditsMessageId, BuildAtlasEditsPayload());
                conn.Send(new SeedSynchronizer.SeedMessage { Value = SeedSynchronizer.Seed });
            }
            catch (Exception ex)
            {
                Logger.Error($"FacilityLayout.SendBootstrap: failed to send recipe/seed to connection {conn.connectionId}: {ex}");
            }
        }

        private static byte[] BuildRecipePayload()
        {
            var msg = new FacilityLayoutRecipeMessage();
            foreach (var e in _entries.Values)
            {
                var wire = new FacilityRecipeEntry
                {
                    ZoneId    = (byte)e.Zone,
                    CoordsX   = (short)e.Coords.x,
                    CoordsY   = (short)e.Coords.y,
                    OpType    = (byte)e.OpType,
                    DirtyMask = e.DirtyMask,
                    OffsetX   = e.Offset.x,
                    OffsetY   = e.Offset.y,
                    OffsetZ   = e.Offset.z,
                    RotationY = e.RotationY
                };
                msg.Entries.Add(wire);
            }
            return SerializeMessage(msg);
        }

        private static byte[] BuildAtlasEditsPayload()
        {
            var msg = new FacilityAtlasEditsMessage();
            foreach (var e in _atlasEdits) msg.Edits.Add(e);
            return SerializeMessage(msg);
        }

        private static void SendBootstrapPayload(NetworkConnectionToClient conn, ushort messageId, byte[] payload)
        {
            using var writer = NetworkWriterPool.Get();
            NetworkWriterExtensions.WriteUShort(writer, messageId);
            NetworkWriterExtensions.WriteBytesAndSize(writer, payload);
            conn.Send(writer.ToArraySegment(), 0);
        }

        private static byte[] SerializeMessage(IAnomalyMessage message)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            message.Serialize(writer);
            writer.Flush();
            return ms.ToArray();
        }

        private static ushort GetMessageId(Type type)
        {
            unchecked
            {
                int hash = 23;
                string name = type.FullName ?? type.Name;
                for (int i = 0; i < name.Length; i++)
                    hash = hash * 31 + name[i];
                return (ushort)hash;
            }
        }

        // ---- Internals --------------------------------------------------

        private static void RequirePreGeneration()
        {
            if (SeedSynchronizer.MapGenerated)
                throw new InvalidOperationException(
                    "FacilityLayout mutations are pre-generation only. Call during ServerEvents.MapGenerating.");
        }

        private static void OnRoundRestarted()
        {
            ClearRecipe();
        }

        private static void OnMapGenerated(LabApi.Events.Arguments.ServerEvents.MapGeneratedEventArgs _)
        {
            if (_entries.Count > 0 || _atlasEdits.Count > 0)
                Logger.Info($"FacilityLayout: applied {_entries.Count} room ops and {_atlasEdits.Count} atlas pixel edits.");
            InvokeRecipeApplied();
        }
    }
}
