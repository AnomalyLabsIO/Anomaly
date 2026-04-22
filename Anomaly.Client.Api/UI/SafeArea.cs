using UnityEngine;

namespace Anomaly.Client.Api.UI;

/// <summary>
/// Applies the device's safe-area margins to a <see cref="RectTransform"/>.
/// The safe area excludes screen regions occupied by notches, rounded corners,
/// system bars, etc. On desktop this is typically a full-screen rect, but
/// applying it is cheap and harmless.
/// </summary>
public static class SafeArea
{
    /// <summary>
    /// Sets <paramref name="rt"/>'s anchor min/max so it fills the current
    /// safe area. The RectTransform's parent must be the root canvas (or a
    /// full-screen container) for the coordinate math to work.
    /// </summary>
    public static void Apply(RectTransform rt)
    {
        if (rt == null) return;

        Rect safe = Screen.safeArea;
        Vector2 min = safe.position;
        Vector2 max = safe.position + safe.size;

        // Convert to normalized [0,1] coordinates relative to the current screen.
        min.x /= Screen.width;
        min.y /= Screen.height;
        max.x /= Screen.width;
        max.y /= Screen.height;

        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
