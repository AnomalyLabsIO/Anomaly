using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Anomaly.Client.Internal.UI;

/// <summary>Shared presentation for Anomaly-owned controls; does not restyle stock UI.</summary>
internal static class AnomalyUiStyle
{
    internal static readonly Color Panel = new(0.078f, 0.078f, 0.078f, 0.98f);
    internal static readonly Color Input = new(0.031f, 0.031f, 0.031f, 1f);
    internal static readonly Color Text = new(0.906f, 0.914f, 0.933f, 1f);
    internal static readonly Color Secondary = new(0.725f, 0.737f, 0.773f, 1f);
    internal static readonly Color Accent = new(0.835f, 0.4f, 0.051f, 1f);
    internal static readonly Color Error = new(1f, 0.64f, 0.55f, 1f);

    private static TMP_FontAsset _font;

    internal static GameObject MakeUi(string name)
        => new(name, new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });

    internal static TMP_FontAsset ResolveFont()
    {
        if (_font != null) return _font;
        foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font == null) continue;
            if (_font == null) _font = font;
            if (string.Equals(font.name, "Inventory", System.StringComparison.Ordinal))
            {
                _font = font;
                break;
            }
        }
        return _font;
    }

    internal static void ApplyText(TMP_Text text, float size, bool wrapping = true)
    {
        var font = ResolveFont();
        if (font != null)
        {
            text.font = font;
            text.fontSharedMaterial = font.material;
        }
        text.fontSize = size;
        text.fontStyle = FontStyles.Normal;
        text.enableAutoSizing = false;
        text.enableWordWrapping = wrapping;
        text.richText = false;
        text.color = Text;
        text.raycastTarget = false;
    }

    internal static void ApplySelectable(Selectable control)
    {
        control.transition = Selectable.Transition.ColorTint;
        var colors = control.colors;
        colors.normalColor = control.targetGraphic != null ? control.targetGraphic.color : Panel;
        if (control.targetGraphic != null) control.targetGraphic.color = Color.white;
        colors.highlightedColor = new Color(0.30f, 0.20f, 0.10f, 1f);
        colors.selectedColor = Accent;
        colors.pressedColor = new Color(0.63f, 0.28f, 0.02f, 1f);
        colors.disabledColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        control.colors = colors;
        var navigation = control.navigation;
        navigation.mode = Navigation.Mode.Automatic;
        control.navigation = navigation;
    }
}
