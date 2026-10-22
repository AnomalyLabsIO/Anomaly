using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.UI.ModSettings.Internal;
using Anomaly.Client.Internal.UI;
using Anomaly.Client.Patches.Menu.ModSettings;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anomaly.Client.Components.ModSettings;

/// <summary>Feedback and reset lifecycle belonging to one visible preference row.</summary>
[RegisterTypeInIl2Cpp]
public sealed class ModSettingsRowState : MonoBehaviour
{
    public ModSettingsRowState(IntPtr pointer) : base(pointer) { }
    public ModSettingsRowState() { }

    private MelonPreferences_Entry _entry;
    private TMP_Text _value;
    private TMP_Text _feedback;
    private Button _reset;
    private Button _retry;
    private string _validation;
    private LemonAction<object, object> _listener;
    private UnityAction _resetListener;
    private UnityAction _retryListener;

    [HideFromIl2Cpp]
    internal void Initialize(MelonPreferences_Entry entry, TMP_Text value, TMP_Text feedback, Button reset, Button retry)
    {
        _entry = entry;
        _value = value;
        _feedback = feedback;
        _reset = reset;
        _retry = retry;
        _listener = new LemonAction<object, object>(OnEntryChanged);
        entry.OnEntryValueChangedUntyped.Subscribe(_listener);
        _resetListener = DelegateSupport.ConvertDelegate<UnityAction>(new Action(ResetValue));
        reset.onClick.AddListener(_resetListener);
        _retryListener = DelegateSupport.ConvertDelegate<UnityAction>(new Action(ModSettingsSaveScheduler.Flush));
        retry.onClick.AddListener(_retryListener);
        ModSettingsSaveScheduler.SaveStatusChanged += OnSaveStatus;
        Refresh();
    }

    [HideFromIl2Cpp]
    internal void SetValidation(string message)
    {
        _validation = message;
        Refresh();
    }

    [HideFromIl2Cpp]
    internal bool TryWrite(object value)
    {
        try
        {
            if (_entry.Validator != null && !_entry.Validator.IsValid(value))
            {
                SetValidation(Tr.Get(TrKeys.Settings_InvalidValue));
                return false;
            }
            _validation = null;
            if (!Equals(_entry.BoxedValue, value))
            {
                _entry.BoxedValue = value;
                ModSettingsSaveScheduler.RequestSave(_entry.Category);
            }
            Refresh();
            return true;
        }
        catch (Exception exception)
        {
            SetValidation(Tr.Get(TrKeys.Settings_ApplyFailed));
            MelonLogger.Warning($"ModSettings write failed for '{_entry.Identifier}': {exception}");
            return false;
        }
    }

    [HideFromIl2Cpp]
    private void ResetValue()
    {
        try
        {
            _entry.ResetToDefault();
            _validation = null;
            ModSettingsSaveScheduler.RequestSave(_entry.Category);
            Refresh();
        }
        catch (Exception exception)
        {
            SetValidation(Tr.Get(TrKeys.Settings_ResetFailed));
            MelonLogger.Warning($"ModSettings reset failed for '{_entry.Identifier}': {exception}");
        }
    }

    [HideFromIl2Cpp]
    private void OnEntryChanged(object previous, object current) => Refresh();

    [HideFromIl2Cpp]
    private void OnSaveStatus(MelonPreferences_Category category, string _)
    {
        if (ReferenceEquals(category, _entry?.Category)) Refresh();
    }

    [HideFromIl2Cpp]
    private void Refresh()
    {
        if (_entry == null || _value == null) return;
        string defaultValue = _entry.GetDefaultValueAsString();
        string currentValue = ModSettingValue.Format(_entry.BoxedValue);
        _value.text = Tr.Get(TrKeys.Settings_ValueSummary,
            string.IsNullOrEmpty(currentValue) ? Tr.Get(TrKeys.Settings_EmptyValue) : currentValue,
            string.IsNullOrEmpty(defaultValue) ? Tr.Get(TrKeys.Settings_EmptyValue) : defaultValue);
        _reset.interactable = !string.Equals(_entry.GetValueAsString(), _entry.GetDefaultValueAsString(), StringComparison.Ordinal);
        string message = _validation;
        string saveError = ModSettingsSaveScheduler.GetError(_entry.Category);
        _retry.gameObject.SetActive(!string.IsNullOrEmpty(saveError) && ModSettingsSaveScheduler.CanRetry(_entry.Category));
        if (!string.IsNullOrEmpty(saveError))
            message = string.IsNullOrEmpty(message) ? saveError : message + "\n" + saveError;
        _feedback.text = message ?? string.Empty;
        _feedback.color = AnomalyUiStyle.Error;
        _feedback.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    private void OnDestroy()
    {
        ModSettingsSaveScheduler.SaveStatusChanged -= OnSaveStatus;
        if (_entry != null && _listener != null) _entry.OnEntryValueChangedUntyped.Unsubscribe(_listener);
        if (_reset != null && _resetListener != null) _reset.onClick.RemoveListener(_resetListener);
        if (_retry != null && _retryListener != null) _retry.onClick.RemoveListener(_retryListener);
    }
}
