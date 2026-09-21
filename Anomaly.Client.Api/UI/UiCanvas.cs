using UnityEngine;
using UnityEngine.UI;

namespace Anomaly.Client.Api.UI;

/// <summary>
/// Z-ordered layers on the root Anomaly canvas. Higher values render on top.
/// </summary>
public enum UiLayer
{
    /// <summary>Background imagery behind everything else.</summary>
    Background = 0,
    /// <summary>World-space overlays (nameplates, world-anchored markers).</summary>
    World = 100,
    /// <summary>Persistent HUD (health, ammo, ability cooldowns).</summary>
    HUD = 200,
    /// <summary>Transient overlays (notifications, tooltips).</summary>
    Overlay = 300,
    /// <summary>Modal dialogs that should capture focus.</summary>
    Modal = 400,
    /// <summary>Debug overlays, always on top.</summary>
    Debug = 500,
}

/// <summary>
/// Wraps the root Anomaly canvas. Created once during client startup and kept
/// alive across scene loads via <c>UnityEngine.Object.DontDestroyOnLoad</c>. Provides
/// per-layer parent <see cref="RectTransform"/>s that mods use as instantiation
/// targets.
/// </summary>
/// <remarks>
/// <para>This is infrastructure for future UI work (custom HUDs, rebind menus,
/// debug overlays). Plugins don't typically interact with the bare canvas —
/// they get a layer via <see cref="GetLayer"/> and instantiate their prefab
/// under it.</para>
/// </remarks>
public static class UiCanvas
{
    private static GameObject _root;
    private static Canvas _canvas;
    private static readonly System.Collections.Generic.Dictionary<UiLayer, RectTransform> _layers =
        new System.Collections.Generic.Dictionary<UiLayer, RectTransform>();

    /// <summary>True once the canvas exists; false before bootstrap completes.</summary>
    public static bool IsReady => _root != null && _canvas != null;

    /// <summary>The underlying Unity <see cref="Canvas"/>. Null until <see cref="IsReady"/>.</summary>
    public static Canvas Canvas => _canvas;

    /// <summary>The root GameObject for the canvas. Null until <see cref="IsReady"/>.</summary>
    public static GameObject Root => _root;

    /// <summary>
    /// Returns the RectTransform for the given <paramref name="layer"/>,
    /// creating it on first access. Returns null if the canvas isn't ready yet.
    /// </summary>
    public static RectTransform GetLayer(UiLayer layer)
    {
        if (!IsReady) return null;
        if (_layers.TryGetValue(layer, out var rt) && rt != null) return rt;

        var go = new GameObject($"Anomaly.Layer.{layer}");
        go.transform.SetParent(_root.transform, worldPositionStays: false);
        var newRt = go.AddComponent<RectTransform>();
        newRt.anchorMin = Vector2.zero;
        newRt.anchorMax = Vector2.one;
        newRt.offsetMin = Vector2.zero;
        newRt.offsetMax = Vector2.zero;
        newRt.SetSiblingIndex((int)layer);
        _layers[layer] = newRt;
        return newRt;
    }

    /// <summary>
    /// Creates the root canvas if it doesn't exist yet. Internal — called by
    /// <c>UiBootstrap</c>, not by plugins directly.
    /// </summary>
    internal static void EnsureCreated()
    {
        if (_root != null) return;

        _root = new GameObject("Anomaly.Root");
        UnityEngine.Object.DontDestroyOnLoad(_root);

        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1000; // Ensure we sit above the game's default UI.

        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _root.AddComponent<GraphicRaycaster>();
    }
}
