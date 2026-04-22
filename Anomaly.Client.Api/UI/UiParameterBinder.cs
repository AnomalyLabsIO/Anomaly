using System.Collections.Generic;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace Anomaly.Client.Api.UI;

/// <summary>
/// Binds string key-value parameters into a UI instance by looking for child
/// GameObjects named <c>param:&lt;key&gt;</c>. This lets plugins author UI
/// prefabs with named slots in the Unity editor and hand data in at runtime
/// without custom MonoBehaviours.
/// </summary>
/// <remarks>
/// <para>Naming convention: any child whose name starts with <c>"param:"</c>
/// is treated as a slot. The text after the colon is the key.</para>
/// <para>Supported slot components (first match wins):</para>
/// <list type="bullet">
///   <item><see cref="TMP_Text"/> — sets <see cref="TMP_Text.text"/>.</item>
///   <item><see cref="Text"/> — sets <see cref="Text.text"/>.</item>
///   <item><see cref="Image"/> — sets <see cref="Image.color"/> if the value parses as a color.</item>
/// </list>
/// <para>Special key <c>"_active"</c> toggles the slot's <see cref="GameObject.activeSelf"/>
/// based on truthy/falsy string values ("true"/"1"/"yes" vs. others).</para>
/// </remarks>
public static class UiParameterBinder
{
    private const string Prefix = "param:";

    /// <summary>
    /// Applies <paramref name="parameters"/> to matching named children of
    /// <paramref name="root"/>.
    /// </summary>
    public static void Bind(GameObject root, IReadOnlyDictionary<string, string> parameters)
    {
        if (root == null || parameters == null || parameters.Count == 0) return;

        var transforms = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in transforms)
        {
            string name = t.name;
            if (name == null || !name.StartsWith(Prefix, System.StringComparison.Ordinal)) continue;
            string rawKey = name.Substring(Prefix.Length);
            if (rawKey.Length == 0) continue;

            // "_active" suffix: toggle instead of text.
            if (rawKey.EndsWith("._active", System.StringComparison.Ordinal))
            {
                string baseKey = rawKey.Substring(0, rawKey.Length - "._active".Length);
                if (parameters.TryGetValue(baseKey, out var activeValue))
                    t.gameObject.SetActive(ParseBool(activeValue));
                continue;
            }

            if (!parameters.TryGetValue(rawKey, out var value)) continue;
            ApplyToSlot(t.gameObject, value);
        }
    }

    private static void ApplyToSlot(GameObject slot, string value)
    {
        var tmp = slot.GetComponent<TMP_Text>();
        if (tmp != null) { tmp.text = value ?? string.Empty; return; }

        var text = slot.GetComponent<Text>();
        if (text != null) { text.text = value ?? string.Empty; return; }

        var image = slot.GetComponent<Image>();
        if (image != null && !string.IsNullOrEmpty(value))
        {
            if (ColorUtility.TryParseHtmlString(value, out var parsed))
            {
                image.color = parsed;
                return;
            }
        }

        MelonLogger.Warning($"UiParameterBinder: slot '{slot.name}' has no bindable component for value '{value}'.");
    }

    private static bool ParseBool(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        switch (s.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "on":
                return true;
            default:
                return false;
        }
    }
}
