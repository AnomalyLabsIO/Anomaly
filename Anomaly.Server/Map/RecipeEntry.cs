using MapGeneration;
using UnityEngine;

namespace Anomaly.Server.Map
{
    /// <summary>
    /// Server-side representation of a single pre-generation mutation targeting
    /// a specific atlas tile within a specific <see cref="FacilityZone"/>.
    /// Mirrors <see cref="Anomaly.Shared.Networking.Messages.FacilityRecipeEntry"/>
    /// but stores rich Unity types for the plugin API. Converted to the wire
    /// form when broadcast.
    /// </summary>
    internal struct RecipeEntry
    {
        public FacilityZone Zone;
        public Vector2Int   Coords;
        public OpType       OpType;
        public byte         DirtyMask;   // see Anomaly.Shared constants DirtyPosition / DirtyRotation
        public Vector3      Offset;
        public float        RotationY;
    }

    internal enum OpType : byte
    {
        TransformOverride = 0,
        Skip              = 1
    }
}
