using System;
using Anomaly.Client.Api.UI.ModSettings;
using Anomaly.Client.Patches.Menu.ModSettings;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anomaly.Client.Components.ModSettings;

/// <summary>
/// Bidirectional binding between a Unity <see cref="Slider"/> and a numeric
/// <c>MelonPreferences_Entry</c> (<c>int</c>, <c>float</c>, or <c>double</c>).
/// Min/max are taken from <see cref="ModSettingMetadata.MinValue"/>/
/// <see cref="ModSettingMetadata.MaxValue"/> when set, falling back to a
/// <c>MelonLoader.Preferences.ValueRange&lt;T&gt;</c> validator on the entry.
/// </summary>
[RegisterTypeInIl2Cpp]
public sealed class ModSettingsSliderBinder : MonoBehaviour
{
    public ModSettingsSliderBinder(IntPtr intPtr) : base(intPtr) { }
    public ModSettingsSliderBinder() { }

    private MelonPreferences_Entry _entry;
    private Type _entryType;
    private LemonAction<object, object> _externalListener;
    private UnityAction<float> _uiListener;
    private float _min, _max;
    private bool _wholeNumbers;
    private Slider _slider;

    [HideFromIl2Cpp]
    internal void Initialize(MelonPreferences_Entry entry, ModSettingMetadata metadata, float min, float max, bool wholeNumbers)
    {
        _entry = entry;
        _entryType = entry?.GetReflectedType();
        _min = min;
        _max = max;
        _wholeNumbers = wholeNumbers;
    }

    private void Awake()
    {
        if (_entry == null) return;
        _slider = GetComponentInChildren<Slider>(includeInactive: true);
        if (_slider == null) return;

        try
        {
            _slider.wholeNumbers = _wholeNumbers;
            _slider.minValue = _min;
            _slider.maxValue = _max;
            _slider.SetValueWithoutNotify(BoxedToFloat(_entry.BoxedValue));
        }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings slider init failed for '{_entry.Identifier}': {ex.Message}"); }

        _uiListener = DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(OnUiChanged));
        _slider.onValueChanged.AddListener(_uiListener);

        _externalListener = new LemonAction<object, object>(OnExternalChange);
        try { _entry.OnEntryValueChangedUntyped?.Subscribe(_externalListener, priority: 0, unsubscribeOnFirstInvocation: false); }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings slider subscribe failed for '{_entry.Identifier}': {ex.Message}"); }
    }

    [HideFromIl2Cpp]
    private void OnUiChanged(float v)
    {
        if (_entry == null) return;
        try
        {
            object typed = ToEntryType(v);
            if (Equals(_entry.BoxedValue, typed)) return;
            _entry.BoxedValue = typed;
            ModSettingsSaveScheduler.RequestSave(_entry.Category);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ModSettings slider write failed for '{_entry.Identifier}': {ex.Message}");
        }
    }

    [HideFromIl2Cpp]
    private void OnExternalChange(object _, object newVal)
    {
        if (_slider == null) return;
        try { _slider.SetValueWithoutNotify(BoxedToFloat(newVal)); }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings slider refresh failed: {ex.Message}"); }
    }

    private void OnDestroy()
    {
        if (_slider != null && _uiListener != null)
        {
            try { _slider.onValueChanged.RemoveListener(_uiListener); } catch { }
        }
        if (_entry != null && _externalListener != null)
        {
            try { _entry.OnEntryValueChangedUntyped?.Unsubscribe(_externalListener); } catch { }
        }
    }

    [HideFromIl2Cpp]
    private static float BoxedToFloat(object boxed)
    {
        if (boxed == null) return 0f;
        return Convert.ToSingle(boxed);
    }

    [HideFromIl2Cpp]
    private object ToEntryType(float v)
    {
        if (_entryType == null) return v;
        if (_entryType == typeof(float)) return v;
        if (_entryType == typeof(double)) return (double)v;
        if (_entryType == typeof(int)) return (int)Math.Round(v);
        if (_entryType == typeof(long)) return (long)Math.Round(v);
        return Convert.ChangeType(v, _entryType);
    }
}
