using Il2CppHints;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Local subtitle-style text display. Currently implemented as a thin alias
/// over <see cref="LocalHint"/> with subtitle-appropriate defaults (longer
/// duration, fade-in/out effects, plain text). Useful for mods that want to
/// display caption-like text — accessibility transcripts, custom CASSIE
/// subtitling, mod narration — without round-tripping the server.
/// </summary>
/// <remarks>
/// This is intentionally minimal in v1. Once the planned UI prefab library
/// ships (see <c>UiPrefabs</c> in the docs queue), <see cref="Subtitles"/>
/// will migrate to a dedicated bottom-anchored prefab with proper subtitle
/// styling (background panel, system font, multi-line wrap). The API
/// signature here will remain stable across that migration.
/// </remarks>
public static class Subtitles
{
    /// <summary>
    /// Shows a subtitle line for <paramref name="durationSeconds"/>.
    /// Returns false if the local hint display isn't ready.
    /// </summary>
    public static bool Show(string text, float durationSeconds = 4f)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        HintEffect[] effects = BuildFadeEffects(durationSeconds);
        return LocalHint.Show(text, parameters: null, effects: effects, durationSeconds: durationSeconds);
    }

    /// <summary>
    /// Shows a subtitle and lets the caller supply additional <see cref="HintEffect"/>s
    /// on top of the default fade. Useful for mods that want to add a typewriter
    /// or flash effect.
    /// </summary>
    public static bool Show(string text, HintEffect[] additionalEffects, float durationSeconds = 4f)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        HintEffect[] fadeEffects = BuildFadeEffects(durationSeconds);
        HintEffect[] merged = Combine(fadeEffects, additionalEffects);
        return LocalHint.Show(text, parameters: null, effects: merged, durationSeconds: durationSeconds);
    }

    private static HintEffect[] BuildFadeEffects(float durationSeconds)
    {
        try
        {
            // Half a second fade-in / fade-out window, scaled to total duration.
            float fadeWindow = System.Math.Min(0.5f, durationSeconds * 0.25f);
            return HintEffectPresets.FadeInAndOut(window: fadeWindow);
        }
        catch
        {
            return null;
        }
    }

    private static HintEffect[] Combine(HintEffect[] a, HintEffect[] b)
    {
        if (a == null || a.Length == 0) return b;
        if (b == null || b.Length == 0) return a;
        HintEffect[] merged = new HintEffect[a.Length + b.Length];
        System.Array.Copy(a, 0, merged, 0, a.Length);
        System.Array.Copy(b, 0, merged, a.Length, b.Length);
        return merged;
    }
}
