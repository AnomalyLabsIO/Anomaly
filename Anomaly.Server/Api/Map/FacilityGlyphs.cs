using System;
using System.Collections.Generic;
using MapGeneration;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Api.Map
{
    /// <summary>
    /// Read-through view of <c>MapGeneration.MapAtlasInterpreter.Singleton.PairDefinitions</c>,
    /// the color-to-room-shape lookup table the facility generator uses to decode
    /// its atlas textures. Plugins use it to resolve the <see cref="Color32"/>
    /// they should write into an atlas pixel to produce a given room shape or a
    /// specific vanilla <see cref="RoomName"/>.
    /// </summary>
    /// <remarks>
    /// Colors are baked into the game's Unity assets — not literals in the
    /// decompile — so the palette is read from the live singleton on demand
    /// rather than hard-coded here. The singleton is created when the facility
    /// scene loads; calls during very early plugin bootstrap will return
    /// <c>false</c>. Call during <c>ServerEvents.MapGenerating</c> or later.
    /// </remarks>
    public static class FacilityGlyphs
    {
        /// <summary>
        /// A color guaranteed not to match any <see cref="GlyphShapePair"/>
        /// (outside the ±5 tolerance window the interpreter uses). Write this
        /// into a pixel to leave the tile empty — the interpreter silently
        /// skips pixels that don't resolve to a pair.
        /// </summary>
        public static Color32 Empty => new Color32(0, 0, 0, 255);

        /// <summary>
        /// Snapshot of every <see cref="GlyphShapePair"/> registered on the
        /// current <c>MapAtlasInterpreter</c> singleton. Empty if the singleton
        /// hasn't been created yet (i.e. facility scene not loaded).
        /// </summary>
        public static IReadOnlyList<GlyphPairInfo> AllPairs
        {
            get
            {
                var singleton = MapAtlasInterpreter.Singleton;
                if (singleton == null || singleton.PairDefinitions == null)
                    return Array.Empty<GlyphPairInfo>();

                var src  = singleton.PairDefinitions;
                var list = new List<GlyphPairInfo>(src.Length);
                foreach (var p in src) list.Add(ToInfo(p));
                return list;
            }
        }

        /// <summary>
        /// Return the color for the first glyph pair whose <c>RoomShape</c>
        /// matches <paramref name="shape"/>. For shapes that appear multiple
        /// times in the palette (same shape, different rotation sets) this
        /// picks the first; callers that need a specific variant should
        /// enumerate <see cref="AllPairs"/> themselves.
        /// </summary>
        public static bool TryGetColor(RoomShape shape, out Color32 color)
        {
            var singleton = MapAtlasInterpreter.Singleton;
            if (singleton != null && singleton.PairDefinitions != null)
            {
                foreach (var p in singleton.PairDefinitions)
                {
                    if (p.RoomShape == shape)
                    {
                        color = p.GlyphColor;
                        return true;
                    }
                }
            }
            color = default;
            return false;
        }

        /// <summary>
        /// Return the color for the first glyph pair whose <c>SpecificRooms</c>
        /// list contains <paramref name="room"/>. Use this when a plugin wants
        /// to pin a specific vanilla room (e.g. <c>RoomName.LczArmory</c>) to
        /// a given atlas pixel — but only if that room name was registered as
        /// "specific" on one of the pairs; most generic shapes don't list
        /// specific rooms.
        /// </summary>
        public static bool TryGetColor(RoomName room, out Color32 color)
        {
            var singleton = MapAtlasInterpreter.Singleton;
            if (singleton != null && singleton.PairDefinitions != null)
            {
                foreach (var p in singleton.PairDefinitions)
                {
                    if (p.SpecificRooms == null) continue;
                    foreach (var r in p.SpecificRooms)
                    {
                        if (r == room)
                        {
                            color = p.GlyphColor;
                            return true;
                        }
                    }
                }
            }
            color = default;
            return false;
        }

        /// <summary>
        /// Dump the live palette to the server log. Useful one-shot diagnostic
        /// when writing or debugging atlas mutations: you'll see, for each
        /// registered pair, the RGBA triple, the <see cref="RoomShape"/>, the
        /// specific rooms allowed, and the permitted rotations.
        /// </summary>
        public static void LogPalette()
        {
            var pairs = AllPairs;
            if (pairs.Count == 0)
            {
                Logger.Info("FacilityGlyphs: palette is empty (MapAtlasInterpreter singleton not yet initialized).");
                return;
            }

            Logger.Info($"FacilityGlyphs: {pairs.Count} glyph pair(s) registered:");
            for (int i = 0; i < pairs.Count; i++)
            {
                var p = pairs[i];
                Logger.Info($"  [{i}] RGB=({p.Color.r},{p.Color.g},{p.Color.b}) shape={p.Shape} rooms=[{string.Join(", ", p.SpecificRooms)}] rotations=[{string.Join(", ", p.Rotations)}]");
            }
        }

        private static GlyphPairInfo ToInfo(GlyphShapePair p) => new GlyphPairInfo
        {
            Color         = p.GlyphColor,
            Shape         = p.RoomShape,
            SpecificRooms = p.SpecificRooms == null ? Array.Empty<RoomName>() : (RoomName[])p.SpecificRooms.Clone(),
            Rotations     = p.RoomRotations == null ? Array.Empty<float>() : (float[])p.RoomRotations.Clone()
        };
    }

    /// <summary>Detached palette snapshot of one <see cref="GlyphShapePair"/>.</summary>
    public struct GlyphPairInfo
    {
        public Color32      Color;
        public RoomShape    Shape;
        public RoomName[]   SpecificRooms;
        public float[]      Rotations;
    }
}
