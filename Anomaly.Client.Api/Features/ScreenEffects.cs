using System.Collections;
using Anomaly.Client.Api.UI;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Local full-screen color overlays — tint and flash. Implemented via a
/// single <see cref="UnityEngine.UI.Image"/> on <see cref="UiLayer.Overlay"/>,
/// not via HDRP post-processing volumes, so this works regardless of the
/// game's render pipeline state and can't conflict with the engine's own
/// flashbang / vignette effects.
/// </summary>
/// <remarks>
/// <para>For the proper SCP:SL-style screen effects (vignette pulses,
/// chromatic aberration, color grading), HDRP <c>VolumeComponent</c>
/// construction is required — that's deferred to a future batch and tracked
/// in the docs queue as the <c>ScreenEffects.Pulse</c> follow-up.</para>
/// <para>The current implementation uses a single shared overlay
/// <see cref="GameObject"/>; calling <see cref="Tint"/> while a flash is
/// running, or starting two flashes back-to-back, will replace the current
/// effect. Mods that need layered effects should manage their own
/// <see cref="Image"/> children of <see cref="UiLayer.Overlay"/>.</para>
/// </remarks>
public static class ScreenEffects
{
    private const string OverlayName = "AnomalyScreenEffectOverlay";

    private static GameObject _overlay;
    private static Image _image;
    private static object _activeFlashHandle;

    /// <summary>
    /// Sets a persistent full-screen color overlay. Call <see cref="ClearTint"/>
    /// to remove. Returns false if the UI canvas isn't ready.
    /// </summary>
    /// <param name="color">RGB values are used as the overlay color; alpha controls opacity.</param>
    public static bool Tint(Color color)
    {
        if (!EnsureOverlay())
            return false;

        StopFlash();
        _image.color = color;
        _overlay.SetActive(color.a > 0f);
        return true;
    }

    /// <summary>
    /// Convenience overload: tints with a separately-specified alpha so callers
    /// can pass a vivid <see cref="Color"/> without manually mutating its alpha.
    /// </summary>
    public static bool Tint(Color rgb, float alpha)
    {
        Color c = rgb;
        c.a = Mathf.Clamp01(alpha);
        return Tint(c);
    }

    /// <summary>
    /// Drops any active tint and any in-progress flash.
    /// </summary>
    public static bool ClearTint()
    {
        if (_image == null)
            return false;

        StopFlash();
        _image.color = new Color(0f, 0f, 0f, 0f);
        if (_overlay != null)
            _overlay.SetActive(false);
        return true;
    }

    /// <summary>
    /// Animates a one-shot full-screen flash: alpha fades from 0 to
    /// <paramref name="color"/>'s alpha over <paramref name="fadeIn"/>, holds
    /// for <paramref name="hold"/>, then fades back to 0 over
    /// <paramref name="fadeOut"/>. Replaces any in-progress effect on this
    /// shared overlay.
    /// </summary>
    public static bool Flash(Color color, float fadeIn = 0.1f, float hold = 0.2f, float fadeOut = 0.6f)
    {
        if (!EnsureOverlay())
            return false;

        StopFlash();

        _activeFlashHandle = Coroutines.Start(FlashCoroutine(color, fadeIn, hold, fadeOut));
        return true;
    }

    private static IEnumerator FlashCoroutine(Color targetColor, float fadeIn, float hold, float fadeOut)
    {
        if (_image == null || _overlay == null)
            yield break;

        float targetAlpha = Mathf.Clamp01(targetColor.a);
        Color zero = new(targetColor.r, targetColor.g, targetColor.b, 0f);

        _image.color = zero;
        _overlay.SetActive(true);

        if (fadeIn > 0f)
        {
            float t = 0f;
            while (t < fadeIn)
            {
                t += Time.unscaledDeltaTime;
                float alpha = Mathf.Lerp(0f, targetAlpha, Mathf.Clamp01(t / fadeIn));
                _image.color = new Color(targetColor.r, targetColor.g, targetColor.b, alpha);
                yield return null;
            }
        }

        _image.color = new Color(targetColor.r, targetColor.g, targetColor.b, targetAlpha);
        if (hold > 0f)
            yield return new WaitForSecondsRealtime(hold);

        if (fadeOut > 0f)
        {
            float t = 0f;
            while (t < fadeOut)
            {
                t += Time.unscaledDeltaTime;
                float alpha = Mathf.Lerp(targetAlpha, 0f, Mathf.Clamp01(t / fadeOut));
                _image.color = new Color(targetColor.r, targetColor.g, targetColor.b, alpha);
                yield return null;
            }
        }

        _image.color = zero;
        _overlay.SetActive(false);
        _activeFlashHandle = null;
    }

    private static void StopFlash()
    {
        if (_activeFlashHandle != null)
        {
            try { Coroutines.Stop(_activeFlashHandle); }
            catch { /* best-effort */ }
            _activeFlashHandle = null;
        }
    }

    private static bool EnsureOverlay()
    {
        if (_overlay != null && _image != null)
            return true;

        try
        {
            RectTransform parent = UiCanvas.GetLayer(UiLayer.Overlay);
            if (parent == null)
                return false;

            _overlay = new GameObject(OverlayName);
            _overlay.transform.SetParent(parent, worldPositionStays: false);

            RectTransform rt = _overlay.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _image = _overlay.AddComponent<Image>();
            _image.raycastTarget = false;
            _image.color = new Color(0f, 0f, 0f, 0f);
            _overlay.SetActive(false);
            return true;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Warning($"ScreenEffects: failed to create overlay: {ex.Message}");
            _overlay = null;
            _image = null;
            return false;
        }
    }

    /// <summary>
    /// Drops the overlay GameObject. Called by the loader on disconnect; mods
    /// should not need this.
    /// </summary>
    internal static void Reset()
    {
        StopFlash();
        if (_overlay != null)
        {
            try { UnityEngine.Object.Destroy(_overlay); } catch { /* best-effort */ }
        }
        _overlay = null;
        _image = null;
    }
}
