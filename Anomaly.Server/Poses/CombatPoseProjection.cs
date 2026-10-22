using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Anomaly.Shared.Poses;
using HarmonyLib;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.Thirdperson;
using UnityEngine;

namespace Anomaly.Server.Poses;

/// <summary>Wire-only pruning of branches that cannot affect any native primary hitbox.</summary>
internal sealed class CombatPoseProjection
{
    private static readonly PropertyInfo AnimatorProperty = AccessTools.Property(typeof(AnimatedCharacterModel), "Animator");
    private readonly AnimatedCharacterModel _model;
    private readonly HitboxIdentity[] _hitboxes;
    private readonly Transform[] _anchors;
    private readonly HashSet<ushort> _relevant;
    private readonly (Transform Node, Transform Parent)[] _hierarchy;
    private PoseFrame _source;
    private PoseFrame _projected;
    private CombatPoseProjection(AnimatedCharacterModel model, HitboxIdentity[] hitboxes, HashSet<ushort> relevant)
    {
        _model = model; _hitboxes = hitboxes; _anchors = hitboxes.Select(hitbox => hitbox.transform).ToArray(); _relevant = relevant;
        var hierarchy = new Dictionary<Transform, Transform>();
        foreach (Transform anchor in _anchors)
            for (Transform node = anchor; node != null && node != model.transform; node = node.parent)
                hierarchy[node] = node.parent;
        _hierarchy = hierarchy.Select(pair => (pair.Key, pair.Value)).ToArray();
    }
    internal bool IsCurrent(ReferenceHub hub)
    {
        if (_model == null || _model.Pooled || !(hub?.roleManager.CurrentRole is IFpcRole role) ||
            role.FpcModule.CharacterModelInstance != _model || !ReferenceEquals(_model.Hitboxes, _hitboxes)) return false;
        for (int i = 0; i < _anchors.Length; i++)
            if (_hitboxes[i] == null || _hitboxes[i].transform != _anchors[i]) return false;
        foreach (var edge in _hierarchy)
            if (edge.Node == null || edge.Node.parent != edge.Parent) return false;
        return true;
    }
    internal static bool TryCreate(ReferenceHub hub, out CombatPoseProjection projection)
    {
        projection = null;
        if (!(hub?.roleManager.CurrentRole is IFpcRole role) ||
            !(role.FpcModule.CharacterModelInstance is AnimatedCharacterModel model) || model.Pooled ||
            !PoseRigCatalog.TryGetForRole((sbyte)hub.roleManager.CurrentRole.RoleTypeId, out var rig) ||
            model.name.Replace("(Clone)", string.Empty).Trim() != rig.ModelName) return false;
        try
        {
            var animator = AnimatorProperty?.GetValue(model) as Animator;
            var hitboxes = model.Hitboxes;
            if (animator == null || hitboxes == null || hitboxes.Length == 0 || hitboxes.Any(hitbox => hitbox == null)) return false;
            var relevant = new HashSet<ushort>();
            foreach (PoseBoneDescriptor bone in rig.Bones)
            {
                Transform node = bone.Path != null ? model.transform.Find(bone.Path) : animator.GetBoneTransform((HumanBodyBones)bone.HumanoidBone);
                // An unresolved mapping disables pruning: arbitrary caller authorizations are never guessed away.
                if (node == null || node == model.transform || !node.IsChildOf(model.transform)) return false;
                if (hitboxes.Any(hitbox => hitbox.transform == node || hitbox.transform.IsChildOf(node))) relevant.Add(bone.Id);
            }
            // These exact stock rigs have an authored rigid pelvis attachment in the query bridge.
            if (PoseRigidAnchorCatalog.TryGetHips(rig.Id, out _) &&
                hitboxes.Any(hitbox => hitbox.transform.parent == model.transform && hitbox.transform.name == "Hitbox"))
                relevant.Add(0);
            if (relevant.Count == 0) return false;
            projection = new CombatPoseProjection(model, hitboxes, relevant);
            return true;
        }
        catch { return false; }
    }
    internal PoseFrame Project(PoseFrame frame)
    {
        if (!ReferenceEquals(_source, frame))
        {
            _source = frame;
            _projected = PoseProjectionPolicy.Project(frame, _relevant);
        }
        return _projected;
    }
}
