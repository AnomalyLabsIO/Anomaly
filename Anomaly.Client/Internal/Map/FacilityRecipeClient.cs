using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Transport.Messages;
using Il2CppInterop.Runtime;
using Il2CppMapGeneration;
using Il2CppMirror;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Internal.Map
{
    /// <summary>
    /// Receives the server's pre-generation facility recipe over plain Mirror
    /// messages and exposes it to the <c>AtlasZoneGenerator</c> Harmony
    /// patches. The server sends recipe + atlas edits immediately before the
    /// vanilla <c>SeedMessage</c>, so storage here completes before
    /// <c>SeedSynchronizer.ClientReceiveSeed</c> runs <c>GenerateLevel</c>.
    /// </summary>
    internal static class FacilityRecipeClient
    {
        private static readonly ushort RecipeMessageId = GetMessageId(typeof(FacilityRecipeBootstrapMessage));
        private static readonly ushort AtlasEditsMessageId = GetMessageId(typeof(FacilityAtlasEditsBootstrapMessage));

        private static readonly Dictionary<(FacilityZone zone, Vector2IntKey coords), FacilityRecipeEntry> _entries =
            new Dictionary<(FacilityZone, Vector2IntKey), FacilityRecipeEntry>();

        private static readonly List<AtlasPixelEdit> _atlasEdits = new List<AtlasPixelEdit>();

        private static NetworkMessageDelegate _recipeDelegate;
        private static NetworkMessageDelegate _atlasEditsDelegate;

        internal static void RegisterHandlers()
        {
            if (_recipeDelegate == null)
            {
                _recipeDelegate = DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(
                    new Action<NetworkConnection, NetworkReader, int>(RecipeDispatch));
            }

            if (_atlasEditsDelegate == null)
            {
                _atlasEditsDelegate = DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(
                    new Action<NetworkConnection, NetworkReader, int>(AtlasEditsDispatch));
            }

            var handlers = NetworkClient.handlers;
            handlers[RecipeMessageId] = _recipeDelegate;
            handlers[AtlasEditsMessageId] = _atlasEditsDelegate;

            MelonLogger.Msg(
                $"FacilityRecipeClient: registered Mirror bootstrap handlers " +
                $"recipeId={RecipeMessageId} atlasId={AtlasEditsMessageId} totalHandlers={handlers.Count}");
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

        private static void RecipeDispatch(NetworkConnection _, NetworkReader reader, int __)
        {
            try
            {
                ApplyRecipe(DeserializePayload<FacilityLayoutRecipeMessage>(ReadPayload(reader)));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"FacilityRecipeClient: failed to dispatch recipe bootstrap: {ex}");
            }
        }

        private static void AtlasEditsDispatch(NetworkConnection _, NetworkReader reader, int __)
        {
            try
            {
                ApplyAtlasEdits(DeserializePayload<FacilityAtlasEditsMessage>(ReadPayload(reader)));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"FacilityRecipeClient: failed to dispatch atlas-edits bootstrap: {ex}");
            }
        }

        private static void ApplyRecipe(FacilityLayoutRecipeMessage recipe)
        {
            _entries.Clear();
            foreach (var e in recipe.Entries)
            {
                var key = ((FacilityZone)e.ZoneId, new Vector2IntKey(e.CoordsX, e.CoordsY));
                _entries[key] = e;
            }

            MelonLogger.Msg($"FacilityRecipeClient: received {recipe.Entries.Count} recipe entries.");
        }

        private static void ApplyAtlasEdits(FacilityAtlasEditsMessage edits)
        {
            _atlasEdits.Clear();
            _atlasEdits.AddRange(edits.Edits);
            MelonLogger.Msg($"FacilityRecipeClient: received {edits.Edits.Count} atlas pixel edits.");
        }

        private static T DeserializePayload<T>(byte[] payload) where T : IAnomalyMessage, new()
        {
            var message = new T();
            using var ms = new MemoryStream(payload ?? System.Array.Empty<byte>());
            using var reader = new BinaryReader(ms);
            message.Deserialize(reader);
            return message;
        }

        private static byte[] ReadPayload(NetworkReader reader)
        {
            var payload = NetworkReaderExtensions.ReadBytesAndSize(reader);
            return payload ?? Array.Empty<byte>();
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
