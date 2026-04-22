using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Api.UI;

/// <summary>
/// Lightweight sanity checks for UI prefabs loaded from plugin asset bundles.
/// Prefabs from untrusted sources can pull in disallowed components (cameras,
/// rigidbodies, audio sources) or be unbounded in size, so we validate before
/// instantiating under the Anomaly canvas.
/// </summary>
/// <remarks>
/// The set of disallowed component types is intentionally conservative in v1 —
/// plugins that need specific components beyond basic UI should pass a
/// custom filter or instantiate under their own canvas, not the shared one.
/// </remarks>
public static class UiPrefabValidator
{
    /// <summary>Maximum pixel dimension for any RectTransform in the prefab.</summary>
    public const float MaxDimensionPx = 8192f;

    /// <summary>
    /// Runs validation against <paramref name="prefab"/>. Returns true if the
    /// prefab passes every check. On failure the reason is written to
    /// <paramref name="reason"/> and logged via MelonLogger.
    /// </summary>
    public static bool Validate(GameObject prefab, out string reason)
    {
        if (prefab == null)
        {
            reason = "Prefab is null.";
            return Fail(reason);
        }

        var rt = prefab.GetComponent<RectTransform>();
        if (rt == null)
        {
            reason = "Prefab is missing a root RectTransform.";
            return Fail(reason);
        }

        // Disallowed components anywhere in the hierarchy.
        if (prefab.GetComponentInChildren<Camera>(true) != null)
        {
            reason = "Prefab contains a Camera.";
            return Fail(reason);
        }
        if (prefab.GetComponentInChildren<AudioSource>(true) != null)
        {
            reason = "Prefab contains an AudioSource.";
            return Fail(reason);
        }
        if (prefab.GetComponentInChildren<Rigidbody>(true) != null ||
            prefab.GetComponentInChildren<Rigidbody2D>(true) != null)
        {
            reason = "Prefab contains a Rigidbody.";
            return Fail(reason);
        }

        // Size sanity: reject obviously-broken prefabs that would swallow the screen.
        var rects = prefab.GetComponentsInChildren<RectTransform>(true);
        foreach (var r in rects)
        {
            var size = r.rect.size;
            if (Mathf.Abs(size.x) > MaxDimensionPx || Mathf.Abs(size.y) > MaxDimensionPx)
            {
                reason = $"Prefab RectTransform '{r.name}' exceeds {MaxDimensionPx}px on an axis (got {size}).";
                return Fail(reason);
            }
        }

        reason = null;
        return true;
    }

    private static bool Fail(string reason)
    {
        MelonLogger.Warning($"UiPrefabValidator: {reason}");
        return false;
    }
}
