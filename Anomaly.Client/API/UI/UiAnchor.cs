using UnityEngine;

namespace Anomaly.Client.API.UI;

/// <summary>
/// Canonical anchor positions for UI placement. Each anchor maps to a fixed
/// <see cref="Vector2"/> in the [0,1] anchor/pivot space.
/// </summary>
public enum UiAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

/// <summary>
/// Helpers for applying <see cref="UiAnchor"/> values to a <see cref="RectTransform"/>.
/// </summary>
public static class UiAnchorExtensions
{
    /// <summary>
    /// Sets anchorMin, anchorMax, and pivot to the canonical point for
    /// <paramref name="anchor"/> and optionally applies <paramref name="offset"/>
    /// as anchoredPosition.
    /// </summary>
    public static void Apply(this RectTransform rt, UiAnchor anchor, Vector2 offset = default)
    {
        var point = ToPoint(anchor);
        rt.anchorMin = point;
        rt.anchorMax = point;
        rt.pivot = point;
        rt.anchoredPosition = offset;
    }

    /// <summary>Returns the [0,1] anchor point corresponding to <paramref name="anchor"/>.</summary>
    public static Vector2 ToPoint(UiAnchor anchor)
    {
        switch (anchor)
        {
            case UiAnchor.TopLeft:      return new Vector2(0f, 1f);
            case UiAnchor.TopCenter:    return new Vector2(0.5f, 1f);
            case UiAnchor.TopRight:     return new Vector2(1f, 1f);
            case UiAnchor.MiddleLeft:   return new Vector2(0f, 0.5f);
            case UiAnchor.Center:       return new Vector2(0.5f, 0.5f);
            case UiAnchor.MiddleRight:  return new Vector2(1f, 0.5f);
            case UiAnchor.BottomLeft:   return new Vector2(0f, 0f);
            case UiAnchor.BottomCenter: return new Vector2(0.5f, 0f);
            case UiAnchor.BottomRight:  return new Vector2(1f, 0f);
            default:                    return new Vector2(0.5f, 0.5f);
        }
    }
}
