using System;
using System.Collections.Generic;
using Anomaly.Shared.Networking.Messages;
using Il2CppMapGeneration;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Internal.Map
{
    /// <summary>
    /// Stores facility recipe state for the client patches. The former plain-Mirror,
    /// pre-auth bootstrap receiver is intentionally disabled in v1; any future receiver
    /// must use the authenticated Anomaly transport before mutating generation state.
    /// </summary>
    internal static class FacilityRecipeClient
    {
        private static readonly Dictionary<(FacilityZone zone, Vector2IntKey coords), FacilityRecipeEntry> _entries =
            new Dictionary<(FacilityZone, Vector2IntKey), FacilityRecipeEntry>();

        private static readonly List<AtlasPixelEdit> _atlasEdits = new List<AtlasPixelEdit>();
        private static bool _disabledWarningLogged;

        internal static void RegisterHandlers()
        {
            Clear();
            if (!_disabledWarningLogged)
            {
                _disabledWarningLogged = true;
                MelonLogger.Warning(
                    "FacilityRecipeClient: facility recipe/atlas bootstrap is disabled in v1; " +
                    "plain-Mirror pre-auth handlers were not registered.");
            }
        }

        internal static bool TryGet(FacilityZone zone, Vector2Int coords, out FacilityRecipeEntry entry)
            => _entries.TryGetValue((zone, new Vector2IntKey(coords)), out entry);

        internal static void Clear()
        {
            _entries.Clear();
            _atlasEdits.Clear();
        }

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
        /// Dictionary-friendly wrapper around <see cref="Vector2Int"/>. The
        /// Il2Cpp-generated <c>Vector2Int</c> doesn't implement managed
        /// <see cref="IEquatable{T}"/> or a fast <see cref="object.GetHashCode"/>,
        /// so using it directly as a Dictionary key is lossy.
        /// </summary>
        internal readonly struct Vector2IntKey : System.IEquatable<Vector2IntKey>
        {
            public readonly int X;
            public readonly int Y;

            public Vector2IntKey(int x, int y) { X = x; Y = y; }
            public Vector2IntKey(Vector2Int v) : this(v.x, v.y) { }

            public bool Equals(Vector2IntKey other) => X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is Vector2IntKey k && Equals(k);
            public override int GetHashCode() => (X * 397) ^ Y;
        }
    }
}
