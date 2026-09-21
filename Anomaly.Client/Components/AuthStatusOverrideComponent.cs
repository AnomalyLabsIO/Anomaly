using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Internal.Localization;
using Il2CppInterop.Runtime.Attributes;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using ClientFeature = Anomaly.Client.Api.Features.Client;

namespace Anomaly.Client.Components;

[RegisterTypeInIl2Cpp]
public class AuthStatusOverrideComponent : MonoBehaviour
{
    private const string AuthStatusTextPath = "Servers/Auth Status/Text (TMP)";

    private TMP_Text _label;

    private string _defaultText;
    private bool _hasDefaultText;

    private bool _isOverrideActive;
    private string _overrideText;

    public AuthStatusOverrideComponent(IntPtr intPtr) : base(intPtr)
    {
    }

    public AuthStatusOverrideComponent()
    {
    }

    private void LateUpdate()
    {
        if (!_isOverrideActive && !_hasDefaultText)
            return;

        if (!TryResolveLabel())
            return;

        string desired = _isOverrideActive ? _overrideText : _defaultText;
        if (_label.text != desired)
            _label.text = desired;
    }

    [HideFromIl2Cpp]
    public void BeginOverride(string message)
    {
        _overrideText = message;
        _isOverrideActive = true;

        TryResolveLabel();
        ApplyCurrent();
    }

    [HideFromIl2Cpp]
    public void EndOverride()
    {
        _isOverrideActive = false;
        ApplyCurrent();
    }

    [HideFromIl2Cpp]
    public void SetDefault(string text)
    {
        _defaultText = text;
        _hasDefaultText = true;

        TryResolveLabel();
        ApplyCurrent();
    }

    [HideFromIl2Cpp]
    public void ClearDefault()
    {
        _defaultText = null;
        _hasDefaultText = false;
    }

    [HideFromIl2Cpp]
    public void Refresh()
    {
        TryResolveLabel();
        ApplyCurrent();
    }

    [HideFromIl2Cpp]
    private bool TryResolveLabel()
    {
        if (_label != null)
            return true;

        var mainMenu = ClientFeature.MainMenu;
        if (mainMenu == null)
            return false;

        var labelTransform = mainMenu.transform.Find(AuthStatusTextPath);
        if (labelTransform == null)
            return false;

        _label = labelTransform.GetComponent<TMP_Text>();
        if (_label != null) _label.richText = false;
        return _label != null;
    }

    [HideFromIl2Cpp]
    private void ApplyCurrent()
    {
        if (_label == null)
            return;

        if (_isOverrideActive)
        {
            if (_label.text != _overrideText)
                _label.text = _overrideText;
        }
        else if (_hasDefaultText)
        {
            if (_label.text != _defaultText)
                _label.text = _defaultText;
        }
    }
}

public static class AuthStatusOverlay
{
    public static void BeginAuthenticating()
    {
        var component = EnsureComponent();
        component?.BeginOverride(Tr.Get(TrKeys.Overlay_Authenticating));
    }

    public static void EndAuthenticating(bool restoreOriginalText = true)
    {
        // restoreOriginalText kept for API compatibility with existing callers.
        // The persistent default (if set via SetDefault) takes over automatically
        // when the override releases; if no default is set the label simply stops
        // being enforced and SCPSL's text returns.
        _ = restoreOriginalText;
        var component = GetComponent();
        component?.EndOverride();
    }

    public static void SetDefault(string text)
    {
        var component = EnsureComponent();
        component?.SetDefault(text);
    }

    public static void ClearDefault()
    {
        var component = GetComponent();
        component?.ClearDefault();
    }

    public static void Refresh()
    {
        var component = EnsureComponent();
        component?.Refresh();
    }

    private static AuthStatusOverrideComponent GetComponent()
    {
        var mainMenu = ClientFeature.MainMenu;
        return mainMenu == null ? null : mainMenu.GetComponent<AuthStatusOverrideComponent>();
    }

    private static AuthStatusOverrideComponent EnsureComponent()
    {
        var mainMenu = ClientFeature.MainMenu;
        if (mainMenu == null)
            return null;

        var component = mainMenu.GetComponent<AuthStatusOverrideComponent>();
        return component ?? mainMenu.gameObject.AddComponent<AuthStatusOverrideComponent>();
    }
}
