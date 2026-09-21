using System.Collections.Generic;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Resolves a <see cref="PlayerAnchor"/> to a concrete <see cref="Transform"/>
/// on a player's character model. Stateless — consumers own attachment
/// lifetime and any role-change re-resolution. Mirrors the design of
/// <see cref="Anomaly.Client.Api.Rendering.MaterialTint"/>.
/// </summary>
/// <remarks>
/// Returns <c>null</c> (and <see cref="TryGet(Player, PlayerAnchor, out Transform)"/>
/// returns false) when:
/// <list type="bullet">
///   <item>The player has no character model (SCP-079, dead spectator).</item>
///   <item>The character model is not <see cref="AnimatedCharacterModel"/> (custom rigs).</item>
///   <item>The Humanoid bone is not present on this rig.</item>
///   <item>The named child transform does not exist on this role's prefab.</item>
/// </list>
/// All four are documented behaviour, not error conditions.
/// </remarks>
public static class PlayerAnchors
{
    private enum AnchorKind
    {
        Bone,
        ChildName,
    }

    private readonly struct Mapping
    {
        public readonly AnchorKind Kind;
        public readonly HumanBodyBones Bone;
        public readonly string Name;

        public Mapping(HumanBodyBones bone)
        {
            Kind = AnchorKind.Bone;
            Bone = bone;
            Name = null;
        }

        public Mapping(string name)
        {
            Kind = AnchorKind.ChildName;
            Bone = default;
            Name = name;
        }
    }

    private static readonly Dictionary<PlayerAnchor, Mapping> _map = new()
    {
        // Spine
        [PlayerAnchor.Hips] = new Mapping(HumanBodyBones.Hips),
        [PlayerAnchor.Spine] = new Mapping(HumanBodyBones.Spine),
        [PlayerAnchor.Chest] = new Mapping(HumanBodyBones.Chest),
        [PlayerAnchor.UpperChest] = new Mapping(HumanBodyBones.UpperChest),
        [PlayerAnchor.Neck] = new Mapping(HumanBodyBones.Neck),
        [PlayerAnchor.Head] = new Mapping(HumanBodyBones.Head),

        // Face
        [PlayerAnchor.LeftEye] = new Mapping(HumanBodyBones.LeftEye),
        [PlayerAnchor.RightEye] = new Mapping(HumanBodyBones.RightEye),
        [PlayerAnchor.Jaw] = new Mapping(HumanBodyBones.Jaw),

        // Arms
        [PlayerAnchor.LeftShoulder] = new Mapping(HumanBodyBones.LeftShoulder),
        [PlayerAnchor.RightShoulder] = new Mapping(HumanBodyBones.RightShoulder),
        [PlayerAnchor.LeftUpperArm] = new Mapping(HumanBodyBones.LeftUpperArm),
        [PlayerAnchor.RightUpperArm] = new Mapping(HumanBodyBones.RightUpperArm),
        [PlayerAnchor.LeftLowerArm] = new Mapping(HumanBodyBones.LeftLowerArm),
        [PlayerAnchor.RightLowerArm] = new Mapping(HumanBodyBones.RightLowerArm),
        [PlayerAnchor.LeftHand] = new Mapping(HumanBodyBones.LeftHand),
        [PlayerAnchor.RightHand] = new Mapping(HumanBodyBones.RightHand),

        // Legs
        [PlayerAnchor.LeftUpperLeg] = new Mapping(HumanBodyBones.LeftUpperLeg),
        [PlayerAnchor.RightUpperLeg] = new Mapping(HumanBodyBones.RightUpperLeg),
        [PlayerAnchor.LeftLowerLeg] = new Mapping(HumanBodyBones.LeftLowerLeg),
        [PlayerAnchor.RightLowerLeg] = new Mapping(HumanBodyBones.RightLowerLeg),
        [PlayerAnchor.LeftFoot] = new Mapping(HumanBodyBones.LeftFoot),
        [PlayerAnchor.RightFoot] = new Mapping(HumanBodyBones.RightFoot),
        [PlayerAnchor.LeftToes] = new Mapping(HumanBodyBones.LeftToes),
        [PlayerAnchor.RightToes] = new Mapping(HumanBodyBones.RightToes),

        // SCP:SL-specific named children
        [PlayerAnchor.Back] = new Mapping("backpack"),
        [PlayerAnchor.ChestRadio] = new Mapping("radio"),
        [PlayerAnchor.ChestFilters] = new Mapping("Filters"),
        [PlayerAnchor.HeadTube] = new Mapping("tube"),
        [PlayerAnchor.Eyewear] = new Mapping("Eyewear"),
        [PlayerAnchor.RightHandGun] = new Mapping("Gun"),
        [PlayerAnchor.RightHandGunHandle] = new Mapping("GunHandle"),
        [PlayerAnchor.RightHandGunHandle2] = new Mapping("GunHandle 2"),
        [PlayerAnchor.RightHandMag] = new Mapping("Mag"),
        [PlayerAnchor.RightHandPistolAmmo] = new Mapping("pistolammo"),
        [PlayerAnchor.RightHandRifleAmmo] = new Mapping("rifleammo"),
        [PlayerAnchor.RightHandItem] = new Mapping("Hand Item Spawnpoint"),
        [PlayerAnchor.RightThighHolster] = new Mapping("Holster"),
    };

    // Cache of resolved child-name transforms per CharacterModel. Stays small
    // (one entry per live model, evicted opportunistically when the cached
    // Transform becomes null after pooling/destruction).
    private static readonly Dictionary<CharacterModel, Dictionary<string, Transform>> _childCache = new();

    public static Transform Get(Player player, PlayerAnchor anchor)
    {
        TryGet(player, anchor, out var t);
        return t;
    }

    public static bool TryGet(Player player, PlayerAnchor anchor, out Transform transform)
    {
        if (player == null)
        {
            transform = null;
            return false;
        }
        return TryGet(player.CharacterModel, anchor, out transform);
    }

    public static Transform Get(CharacterModel model, PlayerAnchor anchor)
    {
        TryGet(model, anchor, out var t);
        return t;
    }

    public static bool TryGet(CharacterModel model, PlayerAnchor anchor, out Transform transform)
    {
        transform = null;
        if (model == null)
            return false;
        if (!_map.TryGetValue(anchor, out var mapping))
            return false;

        var animated = model as AnimatedCharacterModel;
        if (animated == null)
            return false;

        var animator = animated.Animator;
        if (animator == null)
            return false;

        if (mapping.Kind == AnchorKind.Bone)
        {
            transform = animator.GetBoneTransform(mapping.Bone);
            return transform != null;
        }

        transform = ResolveChildByName(model, animator, mapping.Name);
        return transform != null;
    }

    private static Transform ResolveChildByName(CharacterModel model, Animator animator, string name)
    {
        if (!_childCache.TryGetValue(model, out var cache))
        {
            cache = new Dictionary<string, Transform>();
            _childCache[model] = cache;
        }

        if (cache.TryGetValue(name, out var cached))
        {
            // Pooling can destroy the cached transform without touching us.
            // A null check on the cached entry both detects that case and lets
            // a re-walk pick up a freshly-instantiated one.
            if (cached != null)
                return cached;
            cache.Remove(name);
        }

        // Try the primary armature first (start at the Hips bone's parent so
        // we exclude the duplicate skeletons under wearable containers).
        Transform found = null;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var rigRoot = hips != null ? hips.parent : null;
        if (rigRoot != null)
            found = FindByName(rigRoot, name);

        // Fall back to a full-tree walk for role-specific or wearable-attached
        // mounts that live outside the primary armature.
        if (found == null)
            found = FindByName(model.transform, name);

        if (found != null)
            cache[name] = found;
        return found;
    }

    private static Transform FindByName(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        if (all == null)
            return null;
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t == null)
                continue;
            if (t.name == name)
                return t;
        }
        return null;
    }

    internal static bool Unregister(CharacterModel model) =>
        model != null && _childCache.Remove(model);

    internal static void ClearAll() => _childCache.Clear();
}
