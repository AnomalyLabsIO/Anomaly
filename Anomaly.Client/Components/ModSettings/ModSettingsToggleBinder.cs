using System;
using Anomaly.Client.Api.UI.ModSettings;
using Anomaly.Client.Patches.Menu.ModSettings;
using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace Anomaly.Client.Components.ModSettings;

/// <summary>
/// Bidirectional binding between a hand-built "two buttons" widget and a
/// <c>MelonPreferences_Entry&lt;bool&gt;</c>. The renderer supplies the two
/// <see cref="Image"/> swatches (TRUE on the right, FALSE on the left) and
/// wires each button's <c>onClick</c> to <see cref="SetValue"/>; this binder
/// flips the entry, recolors the swatches, and listens for external changes.
/// </summary>
[RegisterTypeInIl2Cpp]
public sealed class ModSettingsToggleBinder : MonoBehaviour
{
    public ModSettingsToggleBinder(IntPtr intPtr) : base(intPtr) { }
    public ModSettingsToggleBinder() { }

    /// <summary>Tint applied to the side matching the current entry value.</summary>
    public static readonly Color ActiveColor = new Color(0.85f, 0.40f, 0.05f, 1.00f);
    /// <summary>Tint applied to the side NOT matching the current entry value.</summary>
    public static readonly Color InactiveColor = new Color(0.10f, 0.10f, 0.10f, 0.85f);

    private MelonPreferences_Entry _entry;
    private Image _trueImage;
    private Image _falseImage;
    private LemonAction<object, object> _externalListener;

    [HideFromIl2Cpp]
    internal void Initialize(MelonPreferences_Entry entry, Image trueImage, Image falseImage)
    {
        _entry = entry;
        _trueImage = trueImage;
        _falseImage = falseImage;
    }

    private void Awake()
    {
        if (_entry == null) return;

        ApplyVisuals(CurrentBool());

        _externalListener = new LemonAction<object, object>(OnExternalChange);
        try { _entry.OnEntryValueChangedUntyped?.Subscribe(_externalListener, priority: 0, unsubscribeOnFirstInvocation: false); }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings bool subscribe failed for '{_entry.Identifier}': {ex.Message}"); }
    }

    /// <summary>Called by each button's <c>onClick</c> handler wired up in the renderer.</summary>
    [HideFromIl2Cpp]
    internal void SetValue(bool value)
    {
        if (_entry == null) return;
        try
        {
            if (Equals(_entry.BoxedValue, value)) return;
            _entry.BoxedValue = value;
            ApplyVisuals(value);
            ModSettingsSaveScheduler.RequestSave(_entry.Category);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ModSettings bool write failed for '{_entry.Identifier}': {ex.Message}");
        }
    }

    [HideFromIl2Cpp]
    private bool CurrentBool()
    {
        try { return Convert.ToBoolean(_entry.BoxedValue); }
        catch { return false; }
    }

    [HideFromIl2Cpp]
    private void OnExternalChange(object _, object newVal)
    {
        try { ApplyVisuals(Convert.ToBoolean(newVal)); }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings bool refresh failed: {ex.Message}"); }
    }

    [HideFromIl2Cpp]
    private void ApplyVisuals(bool isOn)
    {
        if (_trueImage != null) _trueImage.color = isOn ? ActiveColor : InactiveColor;
        if (_falseImage != null) _falseImage.color = isOn ? InactiveColor : ActiveColor;
    }

    private void OnDestroy()
    {
        if (_entry != null && _externalListener != null)
        {
            try { _entry.OnEntryValueChangedUntyped?.Unsubscribe(_externalListener); } catch { }
        }
    }
}
