namespace Anomaly.Client.Api.Features;

/// <summary>
/// Identifies an attachment point on a player's character model. Used by
/// <see cref="PlayerAnchors"/> to resolve a <see cref="UnityEngine.Transform"/>
/// that custom visuals (cosmetic models, item attachments, etc.) can be
/// parented under.
/// </summary>
/// <remarks>
/// <para>
/// The first group resolves through Unity's standard Humanoid avatar via
/// <c>Animator.GetBoneTransform(HumanBodyBones)</c> and works on any role
/// whose <see cref="Il2CppPlayerRoles.FirstPersonControl.Thirdperson.CharacterModel"/>
/// is an <see cref="Il2CppPlayerRoles.FirstPersonControl.Thirdperson.AnimatedCharacterModel"/>.
/// Roles with non-Humanoid rigs (e.g. SCP-049 family, SCP-096) may return
/// <c>null</c> for some or all entries — callers must tolerate that.
/// </para>
/// <para>
/// The second group is SCP:SL-specific named transforms that exist as
/// children of the rig (not Humanoid-mapped). They are resolved by name walk
/// and may be absent on roles whose prefab does not include that mount —
/// e.g. <see cref="Eyewear"/> exists only on Scientist, <see cref="RightHandItem"/>
/// only on Guard. <c>null</c> is documented behaviour, not an error.
/// </para>
/// </remarks>
public enum PlayerAnchor
{
    // === Humanoid bones — resolved via Animator.GetBoneTransform(HumanBodyBones) ===

    // Spine
    Hips,
    Spine,
    Chest,
    UpperChest,
    Neck,
    Head,

    // Face
    LeftEye,
    RightEye,
    Jaw,

    // Arms
    LeftShoulder,
    RightShoulder,
    LeftUpperArm,
    RightUpperArm,
    LeftLowerArm,
    RightLowerArm,
    LeftHand,
    RightHand,

    // Legs
    LeftUpperLeg,
    RightUpperLeg,
    LeftLowerLeg,
    RightLowerLeg,
    LeftFoot,
    RightFoot,
    LeftToes,
    RightToes,

    // === SCP:SL-specific named transforms — resolved by primary-armature name walk ===

    /// <summary>"backpack" — child of SpineMiddle. Main equipment anchor on all human roles.</summary>
    Back,

    /// <summary>"radio" — child of Chest. Communication device mount.</summary>
    ChestRadio,

    /// <summary>"Filters" — child of Chest. Gas-mask filter mount.</summary>
    ChestFilters,

    /// <summary>"tube" — child of Head. Gas-mask tube mount.</summary>
    HeadTube,

    /// <summary>"Eyewear" — Scientist only. Glasses / safety-eyewear mount.</summary>
    Eyewear,

    /// <summary>"Gun" — child of Hand.R. Primary weapon mount.</summary>
    RightHandGun,

    /// <summary>"GunHandle" — child of Gun. Sub-mount for handle parts.</summary>
    RightHandGunHandle,

    /// <summary>"GunHandle 2" — Guard / Scientist alt handle.</summary>
    RightHandGunHandle2,

    /// <summary>"Mag" — child of Gun. Magazine mount.</summary>
    RightHandMag,

    /// <summary>"pistolammo" — pistol-ammo pocket near the right hand.</summary>
    RightHandPistolAmmo,

    /// <summary>"rifleammo" — rifle-ammo pocket near the right hand.</summary>
    RightHandRifleAmmo,

    /// <summary>"Hand Item Spawnpoint" — Guard only. Generic right-hand item mount.</summary>
    RightHandItem,

    /// <summary>"Holster" — child of Thigh.R. Sidearm holster.</summary>
    RightThighHolster,
}
