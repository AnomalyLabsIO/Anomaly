using Il2CppPlayerRoles.FirstPersonControl.Thirdperson;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Hook for mods that need to write character-model state in sync with
/// SCP:SL's animator update cycle. SCP:SL drives the animator manually from
/// <c>CullingSubcontroller.LateUpdate</c> rather than via Unity's standard
/// animator timing — using these hooks instead of plain
/// <see cref="UnityEngine.MonoBehaviour"/> <c>LateUpdate</c> is the only way
/// to get pre-animator root correction or post-animator final-bone solves
/// that don't get clobbered by the next animator pass.
/// </summary>
/// <remarks>
/// Registered via <see cref="CharacterModels.RegisterLocalHook(IModelHook)"/>;
/// the framework auto-attaches and re-attaches to the local player's
/// <see cref="AnimatedCharacterModel"/> across role changes.
/// </remarks>
public interface IModelHook
{
    /// <summary>
    /// Fires before <c>CullingSubcontroller</c> advances the animator. Use
    /// this for root corrections that the animator pass can then apply on top
    /// (e.g. crouch height adjustment, VR body alignment).
    /// </summary>
    void OnBeforeAnimatorUpdated(AnimatedCharacterModel model);

    /// <summary>
    /// Fires after <c>CullingSubcontroller</c> finishes the animator update.
    /// Use this for final bone overrides (e.g. head-bone-to-HMD snap, hand
    /// pose overrides). Mods that override bones here should call
    /// <see cref="CharacterModels.SyncSecondaryRigs(AnimatedCharacterModel)"/>
    /// afterward so mirrored / helper bones stay in sync.
    /// </summary>
    void OnAnimatorUpdated(AnimatedCharacterModel model);
}
