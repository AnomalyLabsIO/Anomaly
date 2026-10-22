using Anomaly.Client.Api.Features;
using Anomaly.Shared.Poses;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson;
using UnityEngine;

namespace Anomaly.Client.Api.Internal.Poses;

/// <summary>Native query authority is isolated from the visual skeleton and its owner leases.</summary>
internal sealed class CombatPoseRigBinding : IDisposable
{
    private readonly PoseRigBinding _binding;
    private readonly List<PoseMutationScope> _combatQueries = new();
    private readonly CombatRigidHitboxBinding[] _rigid;
    private CombatPoseRigBinding(PoseRigBinding binding)
    { _binding = binding; _rigid = CombatRigidHitboxBinding.Create(binding); }
    internal AnimatedCharacterModel Model => _binding.Model;
    internal PoseRigDescriptor Rig => _binding.Rig;
    internal bool IsCurrent => _binding.IsCurrent && _rigid.All(hitbox => hitbox.Transform != null && hitbox.Transform.parent == Model.transform);
    internal static bool TryCreate(Player player, out CombatPoseRigBinding result)
    {
        result = null;
        if (!PoseRigBinding.TryCreate(player, out var binding)) return false;
        try { result = new CombatPoseRigBinding(binding); return true; }
        catch { binding.Dispose(); return false; }
    }

    /// <summary>Mutates only native bone positions/rotations for the duration of an authorized synchronous query.</summary>
    internal IDisposable BeginQuery(PoseFrame frame)
    {
        if (!IsCurrent || frame == null || frame.RigId != Rig.Id || frame.SchemaVersion != Rig.Version) return null;
        PoseMutationScope transaction = null;
        transaction = PoseMutationScope.Begin(journal =>
        {
            // Capture every mapped bone before writing any parent; nested scopes restore their own baseline.
            foreach (var bone in _binding.NativeBones)
            {
                Transform transform = bone.Transform;
                Vector3 position = transform.localPosition;
                Quaternion rotation = transform.localRotation;
                journal.AddRestore(() =>
                {
                    if (transform != null) { transform.localPosition = position; transform.localRotation = rotation; }
                });
            }
            var rigidPositions = _rigid.Select(hitbox => hitbox.Transform.position).ToArray();
            var rigidRotations = _rigid.Select(hitbox => hitbox.Transform.rotation).ToArray();
            foreach (var hitbox in _rigid)
            {
                Transform transform = hitbox.Transform;
                Vector3 position = transform.localPosition;
                Quaternion rotation = transform.localRotation;
                journal.AddRestore(() =>
                {
                    if (transform != null) { transform.localPosition = position; transform.localRotation = rotation; }
                });
            }
            Transform root = Model.transform;
            var initialPositions = _binding.NativeBones.ToDictionary(bone => bone.Id, bone => bone.Transform.position);
            var initialRotations = _binding.NativeBones.ToDictionary(bone => bone.Id, bone => bone.Transform.rotation);
            var poses = frame.Bones.ToDictionary(pose => pose.BoneId);
            foreach (var bone in _binding.NativeBones)
            {
                if (!poses.TryGetValue(bone.Id, out PoseBone pose) || pose.Weight <= 0 || _binding.Player.IsLocal && !pose.IncludeLocalPlayer) continue;
                Transform transform = bone.Transform;
                if ((pose.Components & PoseComponents.Position) != 0)
                    transform.position = Vector3.Lerp(initialPositions[bone.Id], root.TransformPoint(new Vector3(pose.X, pose.Y, pose.Z)), pose.Weight);
                if ((pose.Components & PoseComponents.Rotation) != 0)
                    transform.rotation = Quaternion.Slerp(initialRotations[bone.Id], root.rotation * new Quaternion(pose.Qx, pose.Qy, pose.Qz, pose.Qw), pose.Weight);
            }
            if (poses.TryGetValue(0, out PoseBone hips) && hips.Weight > 0 && (!_binding.Player.IsLocal || hips.IncludeLocalPlayer))
                for (int i = 0; i < _rigid.Length; i++)
                    _rigid[i].Apply(root, hips, initialPositions[0], initialRotations[0], rigidPositions[i], rigidRotations[i]);
        });
        _combatQueries.Add(transaction);
        return PoseMutationScope.Begin(journal => journal.AddRestore(() =>
        {
            try { transaction.Dispose(); } finally { _combatQueries.Remove(transaction); }
        }));
    }

    public void Dispose()
    {
        // Invalidation runs before native pooling so callbacks cannot leave bones posed for another life.
        bool mutated = _combatQueries.Count > 0;
        List<Exception> failures = null;
        for (int i = _combatQueries.Count - 1; i >= 0; i--)
            try { _combatQueries[i].Dispose(); } catch (Exception ex) { (failures ??= new()).Add(ex); }
        _combatQueries.Clear();
        try { if (mutated) Physics.SyncTransforms(); }
        catch (Exception ex) { (failures ??= new()).Add(ex); }
        _binding.Dispose();
        if (failures != null) throw new AggregateException("Pooled combat pose restoration failed.", failures);
    }
}
