using Anomaly.Client.Api.Features;
using Il2Cpp;
using Il2CppInventorySystem.Items;
using UnityEngine;

namespace Anomaly.Client.Api.Rendering;

/// <summary>
/// Convenience wrapper that bundles "resolve anchor → instantiate visual →
/// parent" into one call. Stateless — consumers own the returned
/// <see cref="GameObject"/> handle and any role-change re-attachment.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="MaterialTint"/>'s style: stateless static helpers that
/// do one thing per call. Consumers own lifecycle subscriptions and must
/// reapply their visual state when the player's model changes.
/// </para>
/// <para>
/// Re-attachment on role change: the old <see cref="Il2CppPlayerRoles.FirstPersonControl.Thirdperson.CharacterModel"/>
/// gets pooled when the player's role changes, taking attached children with
/// it. Consumers wiring up persistent attachments must subscribe to
/// <c>RoleEvents.Changed</c> and re-call <see cref="AttachPickup"/> for the
/// new model.
/// </para>
/// </remarks>
public static class ModelAttachment
{
    /// <summary>
    /// Parents <paramref name="model"/> under <paramref name="player"/>'s
    /// <paramref name="anchor"/> transform with the given local offset.
    /// Returns false if the anchor cannot be resolved (e.g. non-FPC role,
    /// custom rig missing the bone, role-specific mount absent).
    /// </summary>
    public static bool Attach(GameObject model, Player player, PlayerAnchor anchor,
                              Vector3 localPosition = default,
                              Quaternion localRotation = default)
    {
        if (model == null || player == null)
            return false;

        var t = player.GetAnchor(anchor);
        if (t == null)
            return false;

        return AttachToTransform(model, t, localPosition, localRotation);
    }

    /// <summary>
    /// Parents <paramref name="model"/> directly under
    /// <paramref name="anchor"/> with the given local offset.
    /// </summary>
    public static bool AttachToTransform(GameObject model, Transform anchor,
                                         Vector3 localPosition = default,
                                         Quaternion localRotation = default)
    {
        if (model == null || anchor == null)
            return false;

        model.transform.SetParent(anchor, worldPositionStays: false);
        model.transform.localPosition = localPosition;
        // default(Quaternion) is (0,0,0,0) — invalid. Coerce to identity.
        model.transform.localRotation = IsZero(localRotation) ? Quaternion.identity : localRotation;
        return true;
    }

    /// <summary>
    /// Instantiates a visual-only copy of the pickup prefab for
    /// <paramref name="type"/> and parents it under
    /// <paramref name="player"/>'s <paramref name="anchor"/>. Returns
    /// <c>null</c> if the prefab is not found or the anchor cannot be
    /// resolved (in the latter case the clone is destroyed before return so
    /// no orphan GameObject leaks into the scene).
    /// </summary>
    public static GameObject AttachPickup(ItemType type, Player player, PlayerAnchor anchor,
                                          Vector3 localPosition = default,
                                          Quaternion localRotation = default)
    {
        if (player == null)
            return null;

        var anchorT = player.GetAnchor(anchor);
        if (anchorT == null)
            return null;

        var clone = ItemModels.InstantiateVisual(type, anchorT);
        if (clone == null)
            return null;

        clone.transform.localPosition = localPosition;
        clone.transform.localRotation = IsZero(localRotation) ? Quaternion.identity : localRotation;
        return clone;
    }

    /// <summary>
    /// Reverses <see cref="Attach"/>. By default destroys
    /// <paramref name="model"/>; pass <c>destroy: false</c> to merely
    /// unparent it (useful if you want to retain the GameObject for re-use).
    /// </summary>
    public static void Detach(GameObject model, bool destroy = true)
    {
        if (model == null)
            return;

        if (destroy)
        {
            UnityEngine.Object.Destroy(model);
            return;
        }

        model.transform.SetParent(null, worldPositionStays: true);
    }

    private static bool IsZero(Quaternion q) =>
        q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f;
}
