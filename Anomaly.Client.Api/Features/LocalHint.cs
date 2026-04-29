using Il2CppHints;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Local-only hint display, mirroring the surface of LabAPI's
/// <c>Player.SendHint</c> but routed through the local player's
/// <see cref="HintDisplay.ShowLocal(Hint)"/> instead of an RPC. Use this when
/// a mod wants to surface a hint to the local player without round-tripping
/// the server (mod menu prompts, custom keybind hints, error messages, etc.).
/// </summary>
public static class LocalHint
{
    /// <summary>
    /// Shows a plain-text hint to the local player.
    /// </summary>
    /// <param name="text">The text to display. Supports TextMeshPro rich text.</param>
    /// <param name="durationSeconds">How long the hint stays visible. Defaults to 3 seconds.</param>
    /// <returns>True if the hint was dispatched, false if the local player isn't ready yet.</returns>
    public static bool Show(string text, float durationSeconds = 3f)
        => Show(text, null, null, durationSeconds);

    /// <summary>
    /// Shows a hint with optional parameters and effects.
    /// </summary>
    /// <param name="text">The text to display. May reference parameters via the standard hint format.</param>
    /// <param name="parameters">Optional <see cref="HintParameter"/> array for dynamic placeholder values.</param>
    /// <param name="effects">Optional <see cref="HintEffect"/> array (fade, pulse, etc.). See <see cref="HintEffectPresets"/>.</param>
    /// <param name="durationSeconds">How long the hint stays visible. Defaults to 3 seconds.</param>
    /// <returns>True if the hint was dispatched, false if the local player isn't ready yet.</returns>
    public static bool Show(string text, HintParameter[] parameters, HintEffect[] effects, float durationSeconds = 3f)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        HintDisplay display = TryGetDisplay();
        if (display == null)
            return false;

        TextHint hint = new(text, parameters, effects, durationSeconds);
        display.ShowLocal(hint);
        return true;
    }

    /// <summary>
    /// Shows a pre-built <see cref="Hint"/> instance (advanced — when you've
    /// constructed a <see cref="TranslationHint"/> or custom subclass yourself).
    /// </summary>
    public static bool Show(Hint hint)
    {
        if (hint == null)
            return false;

        HintDisplay display = TryGetDisplay();
        if (display == null)
            return false;

        display.ShowLocal(hint);
        return true;
    }

    /// <summary>
    /// Pushes a hint onto the local hint stack without displacing the active
    /// one immediately. The pushed hint becomes active on the next pop.
    /// </summary>
    public static bool Push(Hint hint)
    {
        if (hint == null)
            return false;

        HintDisplay display = TryGetDisplay();
        if (display == null)
            return false;

        display.PushHint(hint);
        return true;
    }

    /// <summary>
    /// Pops the most recently pushed hint, restoring the previous one (if any).
    /// </summary>
    public static bool Pop()
    {
        HintDisplay display = TryGetDisplay();
        if (display == null)
            return false;

        display.PopHint();
        return true;
    }

    private static HintDisplay TryGetDisplay()
    {
        LocalPlayer local = LocalPlayer.Instance;
        if (local == null)
            return null;
        return local.Hints;
    }
}
