using System;
using System.Globalization;
using Anomaly.Client.Api.UI.ModSettings;
using Anomaly.Client.Patches.Menu.ModSettings;
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
/// without a slider range. Parse errors on <c>onEndEdit</c> revert the field to
/// the last known good value rather than writing garbage to the entry.
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

    [HideFromIl2Cpp]
    internal void Initialize(MelonPreferences_Entry entry, ModSettingMetadata _ /* metadata reserved */)
    {
        _entry = entry;
        _entryType = entry?.GetReflectedType();
    }

    private void Awake()
    {
        if (_entry == null) return;
        _input = GetComponentInChildren<TMP_InputField>(includeInactive: true);
        if (_input == null) return;

        try
        {
            _input.SetTextWithoutNotify(StringOf(_entry.BoxedValue));
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
            if (!TryParseAs(_entryType, text, out object typed))
            {
                // Revert to last known good value.
                _input.SetTextWithoutNotify(StringOf(_entry.BoxedValue));
                return;
            }

            if (Equals(_entry.BoxedValue, typed)) return;
            _entry.BoxedValue = typed;
            ModSettingsSaveScheduler.RequestSave(_entry.Category);
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
        try { _input.SetTextWithoutNotify(StringOf(newVal)); }
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
    private static string StringOf(object boxed)
    {
        if (boxed == null) return string.Empty;
        return boxed is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : boxed.ToString();
    }

    [HideFromIl2Cpp]
    private static TMP_InputField.ContentType ChooseContentType(Type t)
    {
        if (t == null) return TMP_InputField.ContentType.Standard;
        if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return TMP_InputField.ContentType.IntegerNumber;
        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return TMP_InputField.ContentType.DecimalNumber;
        return TMP_InputField.ContentType.Standard;
    }

    [HideFromIl2Cpp]
    private static bool TryParseAs(Type t, string text, out object value)
    {
        value = null;
        if (t == null) return false;
        if (t == typeof(string)) { value = text ?? string.Empty; return true; }

        try
        {
            if (t == typeof(int))    { value = int.Parse(text, CultureInfo.InvariantCulture); return true; }
            if (t == typeof(long))   { value = long.Parse(text, CultureInfo.InvariantCulture); return true; }
            if (t == typeof(short))  { value = short.Parse(text, CultureInfo.InvariantCulture); return true; }
            if (t == typeof(float))  { value = float.Parse(text, CultureInfo.InvariantCulture); return true; }
            if (t == typeof(double)) { value = double.Parse(text, CultureInfo.InvariantCulture); return true; }
            if (t == typeof(decimal)){ value = decimal.Parse(text, CultureInfo.InvariantCulture); return true; }

            value = Convert.ChangeType(text, t, CultureInfo.InvariantCulture);
            return value != null;
        }
        catch
        {
            value = null;
            return false;
        }
    }
}
