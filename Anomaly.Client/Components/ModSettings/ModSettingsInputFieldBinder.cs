using System;
using Anomaly.Client.Api.UI.ModSettings;
using Anomaly.Client.Api.UI.ModSettings.Internal;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;

namespace Anomaly.Client.Components.ModSettings;

/// <summary>
/// Bidirectional binding between a TextMeshPro <see cref="TMP_InputField"/> and
/// a <c>MelonPreferences_Entry</c> whose value is a string or a numeric type
/// without a slider range. Invalid input remains editable and never changes the entry.
/// </summary>
[RegisterTypeInIl2Cpp]
public sealed class ModSettingsInputFieldBinder : MonoBehaviour
{
    public ModSettingsInputFieldBinder(IntPtr intPtr) : base(intPtr) { }
    public ModSettingsInputFieldBinder() { }

    private MelonPreferences_Entry _entry;
    private Type _entryType;
    private LemonAction<object, object> _externalListener;
    private UnityAction<string> _uiListener;
    private TMP_InputField _input;
    private ModSettingsRowState _row;
    private bool _bound;
    private ModSettingMetadata _metadata;

    [HideFromIl2Cpp]
    internal void Initialize(MelonPreferences_Entry entry, ModSettingMetadata metadata)
    {
        _entry = entry;
        _entryType = entry?.GetReflectedType();
        _metadata = metadata;
        Bind();
    }

    private void Awake() => Bind();

    [HideFromIl2Cpp]
    private void Bind()
    {
        if (_entry == null || _bound) return;
        _input = GetComponentInChildren<TMP_InputField>(includeInactive: true);
        if (_input == null) return;
        _row = GetComponentInParent<ModSettingsRowState>(includeInactive: true);
        _bound = true;

        try
        {
            _input.SetTextWithoutNotify(ModSettingValue.Format(_entry.BoxedValue));
            _input.contentType = ChooseContentType(_entryType);
        }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings input init failed for '{_entry.Identifier}': {ex.Message}"); }

        _uiListener = DelegateSupport.ConvertDelegate<UnityAction<string>>(new Action<string>(OnEndEdit));
        _input.onEndEdit.AddListener(_uiListener);

        _externalListener = new LemonAction<object, object>(OnExternalChange);
        try { _entry.OnEntryValueChangedUntyped?.Subscribe(_externalListener, priority: 0, unsubscribeOnFirstInvocation: false); }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings input subscribe failed for '{_entry.Identifier}': {ex.Message}"); }
    }

    [HideFromIl2Cpp]
    private void OnEndEdit(string text)
    {
        if (_entry == null || _input == null) return;
        try
        {
            if (!ModSettingValue.TryParse(_entryType, text, out object typed))
            {
                _row?.SetValidation("Enter a valid " + _entryType.Name + " value. The setting was not changed.");
                return;
            }

            if (ModSettingValue.IsNumeric(_entryType) && _metadata?.MinValue is double minimum
                && _metadata.MaxValue is double maximum
                && (Convert.ToDouble(typed) < minimum || Convert.ToDouble(typed) > maximum))
            {
                _row?.SetValidation("Enter a value between " + ModSettingValue.Format(minimum)
                    + " and " + ModSettingValue.Format(maximum) + ". The setting was not changed.");
                return;
            }

            if (_row?.TryWrite(typed) == true)
                _input.SetTextWithoutNotify(ModSettingValue.Format(_entry.BoxedValue));
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ModSettings input write failed for '{_entry.Identifier}': {ex.Message}");
        }
    }

    [HideFromIl2Cpp]
    private void OnExternalChange(object _, object newVal)
    {
        if (_input == null) return;
        try { _input.SetTextWithoutNotify(ModSettingValue.Format(newVal)); }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings input refresh failed: {ex.Message}"); }
    }

    private void OnDestroy()
    {
        if (_input != null && _uiListener != null)
        {
            try { _input.onEndEdit.RemoveListener(_uiListener); } catch { }
        }
        if (_entry != null && _externalListener != null)
        {
            try { _entry.OnEntryValueChangedUntyped?.Unsubscribe(_externalListener); } catch { }
        }
    }

    [HideFromIl2Cpp]
    private static TMP_InputField.ContentType ChooseContentType(Type t)
    {
        if (t == null) return TMP_InputField.ContentType.Standard;
        if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return TMP_InputField.ContentType.IntegerNumber;
        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return TMP_InputField.ContentType.DecimalNumber;
        return TMP_InputField.ContentType.Standard;
    }

}
