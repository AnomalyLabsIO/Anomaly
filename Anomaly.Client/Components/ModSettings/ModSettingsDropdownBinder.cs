using System;
using System.Collections.Generic;
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
/// Bidirectional binding between a TextMeshPro <see cref="TMP_Dropdown"/> and a
/// <c>MelonPreferences_Entry</c> whose value is either an enum or a member of a
/// fixed list of <see cref="ModSettingMetadata.Choices"/>.
/// </summary>
[RegisterTypeInIl2Cpp]
public sealed class ModSettingsDropdownBinder : MonoBehaviour
{
    public ModSettingsDropdownBinder(IntPtr intPtr) : base(intPtr) { }
    public ModSettingsDropdownBinder() { }

    private MelonPreferences_Entry _entry;
    private Type _entryType;
    private string[] _options;       // dropdown labels (enum names or Choices)
    private bool _isEnum;
    private LemonAction<object, object> _externalListener;
    private UnityAction<int> _uiListener;
    private TMP_Dropdown _dropdown;

    [HideFromIl2Cpp]
    internal void Initialize(MelonPreferences_Entry entry, ModSettingMetadata metadata, IReadOnlyList<string> options, bool isEnum)
    {
        _entry = entry;
        _entryType = entry?.GetReflectedType();
        _isEnum = isEnum;

        if (options != null)
        {
            _options = new string[options.Count];
            for (int i = 0; i < options.Count; i++) _options[i] = options[i];
        }
        else
        {
            _options = Array.Empty<string>();
        }
    }

    private void Awake()
    {
        if (_entry == null) return;
        _dropdown = GetComponentInChildren<TMP_Dropdown>(includeInactive: true);
        if (_dropdown == null) return;

        try
        {
            _dropdown.ClearOptions();
            var list = new Il2CppSystem.Collections.Generic.List<string>();
            for (int i = 0; i < _options.Length; i++) list.Add(_options[i]);
            _dropdown.AddOptions(list);
            _dropdown.SetValueWithoutNotify(ResolveCurrentIndex());
            _dropdown.RefreshShownValue();
        }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings dropdown init failed for '{_entry.Identifier}': {ex.Message}"); }

        _uiListener = DelegateSupport.ConvertDelegate<UnityAction<int>>(new Action<int>(OnUiChanged));
        _dropdown.onValueChanged.AddListener(_uiListener);

        _externalListener = new LemonAction<object, object>(OnExternalChange);
        try { _entry.OnEntryValueChangedUntyped?.Subscribe(_externalListener, priority: 0, unsubscribeOnFirstInvocation: false); }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings dropdown subscribe failed for '{_entry.Identifier}': {ex.Message}"); }
    }

    [HideFromIl2Cpp]
    private void OnUiChanged(int index)
    {
        if (_entry == null) return;
        if (index < 0 || index >= _options.Length) return;
        try
        {
            object typed = OptionAt(index);
            if (Equals(_entry.BoxedValue, typed)) return;
            _entry.BoxedValue = typed;
            ModSettingsSaveScheduler.RequestSave(_entry.Category);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ModSettings dropdown write failed for '{_entry.Identifier}': {ex.Message}");
        }
    }

    [HideFromIl2Cpp]
    private void OnExternalChange(object _, object newVal)
    {
        if (_dropdown == null) return;
        try
        {
            _dropdown.SetValueWithoutNotify(ResolveCurrentIndex());
            _dropdown.RefreshShownValue();
        }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings dropdown refresh failed: {ex.Message}"); }
    }

    private void OnDestroy()
    {
        if (_dropdown != null && _uiListener != null)
        {
            try { _dropdown.onValueChanged.RemoveListener(_uiListener); } catch { }
        }
        if (_entry != null && _externalListener != null)
        {
            try { _entry.OnEntryValueChangedUntyped?.Unsubscribe(_externalListener); } catch { }
        }
    }

    [HideFromIl2Cpp]
    private int ResolveCurrentIndex()
    {
        object current = _entry.BoxedValue;
        if (current == null) return 0;

        if (_isEnum)
        {
            string name = current.ToString();
            for (int i = 0; i < _options.Length; i++)
            {
                if (string.Equals(_options[i], name, StringComparison.Ordinal)) return i;
            }
            return 0;
        }

        // Numeric or string Choice → match by string representation.
        string repr = current.ToString();
        for (int i = 0; i < _options.Length; i++)
        {
            if (string.Equals(_options[i], repr, StringComparison.Ordinal)) return i;
        }
        return 0;
    }

    [HideFromIl2Cpp]
    private object OptionAt(int index)
    {
        string raw = _options[index];
        if (_isEnum && _entryType != null) return Enum.Parse(_entryType, raw, ignoreCase: false);
        if (_entryType == typeof(string)) return raw;
        if (_entryType != null && _entryType.IsValueType) return Convert.ChangeType(raw, _entryType);
        return raw;
    }
}
