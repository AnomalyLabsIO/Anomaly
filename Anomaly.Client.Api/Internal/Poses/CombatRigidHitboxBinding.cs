using Anomaly.Shared.Poses;
using UnityEngine;

namespace Anomaly.Client.Api.Internal.Poses;

/// <summary>Authored attachment for stock primary hitboxes parented to the model root instead of a bone.</summary>
internal sealed class CombatRigidHitboxBinding
{
    internal readonly Transform Transform;
    private readonly Vector3 _position;
    private readonly Quaternion _rotation;
    private readonly Vector3 _neutralHipsPosition;
    private readonly Quaternion _neutralHipsRotation;

    private CombatRigidHitboxBinding(Transform transform, Vector3 position, Quaternion rotation,
        Vector3 hipsPosition, Quaternion hipsRotation)
    {
        Transform = transform; _position = position; _rotation = rotation;
        _neutralHipsPosition = hipsPosition; _neutralHipsRotation = hipsRotation;
    }

    internal static CombatRigidHitboxBinding[] Create(PoseRigBinding binding)
    {
        if (!PoseRigidAnchorCatalog.TryGetHips(binding.Rig.Id, out var authored)) return Array.Empty<CombatRigidHitboxBinding>();
        Transform root = binding.Model.transform;
        Transform hips = binding.NativeBones.First(bone => bone.Id == 0).Transform;
        Vector3 neutralPosition = new(authored.X, authored.Y, authored.Z);
        Quaternion neutralRotation = new Quaternion(authored.Qx, authored.Qy, authored.Qz, authored.Qw).normalized;
        // A renderer attached beneath the pelvis would itself move with animation and is not a stable bind-space reference.
        foreach (var renderer in binding.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer == null || renderer.transform.IsChildOf(hips) || renderer.sharedMesh == null) continue;
            try
            {
                var bones = renderer.bones;
                var bindposes = renderer.sharedMesh.bindposes;
                if (bones == null || bindposes == null || bones.Length != bindposes.Length) continue;
                bool found = false;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != hips) continue;
                    Matrix4x4 bind = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix * bindposes[i].inverse;
                    Vector3 position = bind.GetColumn(3);
                    Quaternion rotation = bind.rotation;
                    if (!Finite(position) || !Finite(rotation) || Quaternion.Dot(rotation, rotation) < .5f) continue;
                    neutralPosition = position; neutralRotation = rotation.normalized; found = true; break;
                }
                if (found) break;
            }
            catch { /* Exact stock catalog fallback remains available for unreadable mesh data. */ }
        }
        var result = new List<CombatRigidHitboxBinding>();
        foreach (var hitbox in binding.Model.Hitboxes)
        {
            if (hitbox == null || hitbox.transform == null) throw new InvalidOperationException("Missing primary hitbox.");
            Transform target = hitbox.transform;
            if (target.parent != root || target.name != "Hitbox") continue;
            result.Add(new CombatRigidHitboxBinding(target, root.InverseTransformPoint(target.position),
                Quaternion.Inverse(root.rotation) * target.rotation, neutralPosition, neutralRotation));
        }
        return result.ToArray();
    }

    internal void Apply(Transform root, PoseBone hips, Vector3 initialHipsPosition, Quaternion initialHipsRotation,
        Vector3 initialPosition, Quaternion initialRotation)
    {
        Vector3 position = (hips.Components & PoseComponents.Position) != 0
            ? new Vector3(hips.X, hips.Y, hips.Z) : root.InverseTransformPoint(initialHipsPosition);
        Quaternion rotation = (hips.Components & PoseComponents.Rotation) != 0
            ? new Quaternion(hips.Qx, hips.Qy, hips.Qz, hips.Qw) : Quaternion.Inverse(root.rotation) * initialHipsRotation;
        Quaternion delta = rotation * Quaternion.Inverse(_neutralHipsRotation);
        // Use only a rigid delta: authored root scale/collider dimensions remain native.
        Transform.position = Vector3.Lerp(initialPosition, root.TransformPoint(position + delta * (_position - _neutralHipsPosition)), hips.Weight);
        Transform.rotation = Quaternion.Slerp(initialRotation, root.rotation * delta * _rotation, hips.Weight);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
}
