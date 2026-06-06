using System;
using Il2CppTMPro;
using Il2CppUserSettings.GUIElements;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Patches.Menu.ModSettings;

/// <summary>
/// Snapshots a TMP font reference from the live scene so the renderer can use
/// it when constructing fresh <see cref="TextMeshProUGUI"/> components — fresh
/// TMP instances have <c>font=null</c> in Il2Cpp Unity and render invisibly.
/// </summary>
/// <remarks>
/// All Mods-tab widgets (two-buttons / slider / dropdown / input) are built
/// from primitives in the renderer; no live scene widgets are cloned. That
/// removes every avenue for vanilla state (FPS Limit setter, music volume,
/// SCP spawn preferences, etc.) to bleed into our rows.
/// </remarks>
internal sealed class ModSettingsTemplateCache
{
    internal TMP_FontAsset Font { get; private set; }
    internal Material FontMaterial { get; private set; }
    /// <summary>
    /// A 9-sliced rounded-rect <see cref="Sprite"/> generated procedurally — used
    /// as the source sprite for every panel/button/input <see cref="Image"/> so
    /// the corners are rounded and <c>Image.color</c> can tint the result.
    /// </summary>
    internal Sprite RoundedRectSprite { get; private set; }

    internal bool HasFont => Font != null;

    internal void Refresh(UserSettingsCategories _ /* controller unused — kept for API parity */)
    {
        // Verified in UnityExplorer: vanilla settings labels use the "Inventory"
        // font asset (not Roboto-Bold). Look it up among loaded assets; fall back
        // to grabbing any TMP_Text's font if it isn't loaded for some reason.
        Font = FindFontAssetByName("Inventory");
        if (Font == null)
        {
            var anyTmp = UnityEngine.Object.FindObjectOfType<TMP_Text>();
            if (anyTmp != null)
            {
                Font = anyTmp.font;
                FontMaterial = anyTmp.fontSharedMaterial;
            }
        }
        else
        {
            FontMaterial = Font.material;
        }

        if (RoundedRectSprite == null)
        {
            try { RoundedRectSprite = CreateRoundedRectSprite(sizePx: 32, cornerPx: 3); }
            catch (Exception ex) { MelonLogger.Warning($"ModSettings: rounded sprite creation failed: {ex.Message}"); }
        }

        MelonLogger.Msg($"ModSettings templates: font={(Font != null ? Font.name : "<missing>")}, " +
                        $"rounded={(RoundedRectSprite != null ? "ok" : "<missing>")}.");
    }

    private static TMP_FontAsset FindFontAssetByName(string name)
    {
        var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        if (all == null) return null;
        foreach (var f in all)
        {
            if (f != null && string.Equals(f.name, name, StringComparison.Ordinal))
                return f;
        }
        return null;
    }

    /// <summary>
    /// Bakes a white rounded-rect sprite at runtime. The corners are anti-aliased
    /// against a 1-pixel band so they look smooth at any tint. The returned sprite
    /// is 9-sliced with a border equal to <paramref name="cornerPx"/> so it scales
    /// to any size without distortion.
    /// </summary>
    private static Sprite CreateRoundedRectSprite(int sizePx, int cornerPx)
    {
        var tex = new Texture2D(sizePx, sizePx, TextureFormat.RGBA32, mipChain: false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < sizePx; y++)
        {
            for (int x = 0; x < sizePx; x++)
            {
                int dx = System.Math.Min(x, sizePx - 1 - x);
                int dy = System.Math.Min(y, sizePx - 1 - y);

                float alpha = 1f;
                if (dx < cornerPx && dy < cornerPx)
                {
                    float ex = cornerPx - dx;
                    float ey = cornerPx - dy;
                    float dist = (float)System.Math.Sqrt(ex * ex + ey * ey);
                    if (dist > cornerPx) alpha = 0f;
                    else if (dist > cornerPx - 1f) alpha = cornerPx - dist; // 1-px AA band
                }

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        var border = new Vector4(cornerPx, cornerPx, cornerPx, cornerPx);
        return Sprite.Create(
            tex,
            new Rect(0f, 0f, sizePx, sizePx),
            pivot: new Vector2(0.5f, 0.5f),
            pixelsPerUnit: 100f,
            extrude: 0,
            meshType: SpriteMeshType.FullRect,
            border: border);
    }
}
