using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
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
    private bool _invalidPlaceholder;
    private bool _bound;
    private ModSettingsRowState _row;
    private LemonAction<object, object> _externalListener;
    private UnityAction<int> _uiListener;
    private TMP_Dropdown _dropdown;

    [HideFromIl2Cpp]
    internal void Initialize(MelonPreferences_Entry entry, ModSettingMetadata metadata, IReadOnlyList<string> options, bool isEnum)
    {
        _entry = entry;
        _entryType = entry?.GetReflectedType();

        if (options != null)
        {
            _options = new string[options.Count];
            for (int i = 0; i < options.Count; i++) _options[i] = options[i];
        }
        else
        {
            _options = Array.Empty<string>();
        }
        Bind();
    }

    private void Awake() => Bind();

    [HideFromIl2Cpp]
    private void Bind()
    {
        if (_entry == null || _bound) return;
        _dropdown = GetComponentInChildren<TMP_Dropdown>(includeInactive: true);
        if (_dropdown == null) return;
        _bound = true;
        _row = GetComponentInParent<ModSettingsRowState>(includeInactive: true);

        try
        {
            RefreshOptions();
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
        if (_invalidPlaceholder) index--;
        if (index < 0 || index >= _options.Length) return;
        try
        {
            object typed = ModSettingValue.ChoiceValue(_entryType, _options, index);
            _row?.TryWrite(typed);
            RefreshOptions(clearValidation: false);
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
            RefreshOptions();
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
    private void RefreshOptions(bool clearValidation = true)
    {
        int index = ModSettingValue.ChoiceIndex(_entryType, _options, _entry.BoxedValue);
        _invalidPlaceholder = index < 0;
        _dropdown.ClearOptions();
        var list = new Il2CppSystem.Collections.Generic.List<string>();
        if (_invalidPlaceholder) list.Add(Tr.Get(TrKeys.Settings_SelectValidValue));
        foreach (string option in _options) list.Add(option);
        _dropdown.AddOptions(list);
        _dropdown.SetValueWithoutNotify(_invalidPlaceholder ? 0 : index);
        _dropdown.RefreshShownValue();
        if (_invalidPlaceholder)
            _row?.SetValidation(Tr.Get(TrKeys.Settings_UnavailableChoice));
        else if (clearValidation)
            _row?.SetValidation(null);
    }
}
