using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Anomaly.Shared.Poses;

/// <summary>Components replaced relative to the current character model root; scale is never overridden.</summary>
[Flags]
public enum PoseComponents : byte
{
    /// <summary>Replace the bone's absolute position relative to the model root.</summary>
    Position = 1,
    /// <summary>Replace the bone's absolute rotation relative to the model root.</summary>
    Rotation = 2
}

/// <summary>A final bone transform in model-root space. Rotation is normalized on construction.</summary>
public readonly struct PoseBone
{
    /// <summary>Finite per-axis model-space position limit supported by pose capability 1.1.</summary>
    public const float MaximumPosition = 327.67f;
    /// <summary>Creates a bounded bone pose; normalizes the quaternion and rejects invalid components.</summary>
    /// <remarks>Supply an identity quaternion for position-only poses. The server scope options determine
    /// whether a submitted bone may affect its local viewer; the per-bone flag is used by composed frames.</remarks>
    public PoseBone(ushort boneId, PoseComponents components, float x, float y, float z,
        float qx, float qy, float qz, float qw, float weight = 1, bool includeLocalPlayer = false)
    {
        if (boneId >= PoseFrame.MaxBones || components == 0 || (components & ~(PoseComponents.Position | PoseComponents.Rotation)) != 0)
            throw new ArgumentOutOfRangeException(nameof(boneId));
        if (!Finite(x) || !Finite(y) || !Finite(z) || Math.Abs(x) > MaximumPosition || Math.Abs(y) > MaximumPosition || Math.Abs(z) > MaximumPosition ||
            !Finite(qx) || !Finite(qy) || !Finite(qz) || !Finite(qw) || !Finite(weight) || weight < 0 || weight > 1)
            throw new ArgumentException("Pose components must be finite and inside their bounded range.");
        double length = Math.Sqrt((double)qx * qx + (double)qy * qy + (double)qz * qz + (double)qw * qw);
        if (length < 0.000001) throw new ArgumentException("Pose quaternion must have nonzero length.");
        BoneId = boneId; Components = components; X = x; Y = y; Z = z;
        Qx = (float)(qx / length); Qy = (float)(qy / length); Qz = (float)(qz / length); Qw = (float)(qw / length);
        Weight = weight; IncludeLocalPlayer = includeLocalPlayer;
    }

    /// <summary>The stable slot in the selected rig schema, smaller than 128.</summary>
    public ushort BoneId { get; }
    /// <summary>The components replaced by this contribution.</summary>
    public PoseComponents Components { get; }
    /// <summary>Model-root-relative position on the X axis, in model units.</summary>
    public float X { get; }
    /// <summary>Model-root-relative position on the Y axis, in model units.</summary>
    public float Y { get; }
    /// <summary>Model-root-relative position on the Z axis, in model units.</summary>
    public float Z { get; }
    /// <summary>X component of the normalized model-root-relative quaternion.</summary>
    public float Qx { get; }
    /// <summary>Y component of the normalized model-root-relative quaternion.</summary>
    public float Qy { get; }
    /// <summary>Z component of the normalized model-root-relative quaternion.</summary>
    public float Qz { get; }
    /// <summary>W component of the normalized model-root-relative quaternion.</summary>
    public float Qw { get; }
    /// <summary>Blend weight against native animation, between zero and one.</summary>
    public float Weight { get; }
    /// <summary>Whether this composed bone may be presented to the subject's own client.</summary>
    public bool IncludeLocalPlayer { get; }

    /// <summary>Creates a copy with the specified presentation weight and local-viewer policy.</summary>
    public PoseBone WithWeight(float weight, bool includeLocalPlayer) =>
        new PoseBone(BoneId, Components, X, Y, Z, Qx, Qy, Qz, Qw, weight, includeLocalPlayer);

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>An immutable complete contribution. Bones omitted by an update are released.</summary>
public sealed class PoseFrame
{
    /// <summary>Maximum number of slots and the exclusive upper bound for bone IDs.</summary>
    public const int MaxBones = 128;
    /// <summary>Maximum ASCII length of a rig identifier.</summary>
    public const int MaxRigIdLength = 64;

    /// <summary>Copies and validates a complete contribution, sorting bones by stable ID.</summary>
    /// <remarks>An empty frame releases every bone while retaining the handle. Unknown rig slots
    /// are rejected by the server facade and receiver against the chosen catalog descriptor.</remarks>
    public PoseFrame(string rigId, ushort schemaVersion, IEnumerable<PoseBone> bones)
    {
        if (string.IsNullOrWhiteSpace(rigId) || rigId.Length > MaxRigIdLength ||
            rigId.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == ':' || c == '_')))
            throw new ArgumentException("A bounded, lowercase rig identifier is required.", nameof(rigId));
        if (schemaVersion == 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (bones == null) throw new ArgumentNullException(nameof(bones));
        var copy = bones.Take(MaxBones + 1).ToArray();
        if (copy.Length > MaxBones) throw new ArgumentException("Pose exceeds the bone limit.", nameof(bones));
        var seen = new HashSet<ushort>();
        for (int i = 0; i < copy.Length; i++)
        {
            var b = copy[i];
            // Reject default structs and enforce the same rules as ordinary construction.
            copy[i] = new PoseBone(b.BoneId, b.Components, b.X, b.Y, b.Z, b.Qx, b.Qy, b.Qz, b.Qw, b.Weight, b.IncludeLocalPlayer);
            if (!seen.Add(b.BoneId)) throw new ArgumentException("Duplicate bone ID.", nameof(bones));
        }
        Array.Sort(copy, (a, b) => a.BoneId.CompareTo(b.BoneId));
        RigId = rigId; SchemaVersion = schemaVersion;
        Bones = new ReadOnlyCollection<PoseBone>(copy);
    }

    /// <summary>The versioned catalog's rig identifier.</summary>
    public string RigId { get; }
    /// <summary>The exact rig schema version required by this frame.</summary>
    public ushort SchemaVersion { get; }
    /// <summary>Immutable bone values sorted by ID; omitted slots do not contribute.</summary>
    public IReadOnlyList<PoseBone> Bones { get; }
    /// <summary>Returns a detached copy of this frame.</summary>
    public PoseFrame Clone() => new PoseFrame(RigId, SchemaVersion, Bones);
}
