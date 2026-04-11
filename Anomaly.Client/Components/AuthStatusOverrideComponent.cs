using Anomaly.Client.API.Features;
using Il2CppInterop.Runtime.Attributes;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using ClientFeature = Anomaly.Client.API.Features.Client;

namespace Anomaly.Client.Components;

[RegisterTypeInIl2Cpp]
public class AuthStatusOverrideComponent : MonoBehaviour
{
    private const string AuthStatusTextPath = "Servers/Auth Status/Text (TMP)";

    private TMP_Text _label;
    private string _originalText;
    private bool _hasOriginalText;
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
        if (!_isOverrideActive)
            return;

        if (!TryResolveLabel())
            return;

        if (_label.text != _overrideText)
            _label.text = _overrideText;
    }

    private void OnDestroy()
    {
        RestoreOriginalText();
    }

    [HideFromIl2Cpp]
    public void BeginOverride(string message)
    {
        _overrideText = message;
        _isOverrideActive = true;

        TryResolveLabel();
        ApplyOverride();
    }

    [HideFromIl2Cpp]
    public void EndOverride(bool restoreOriginalText = true)
    {
        _isOverrideActive = false;

        if (restoreOriginalText)
            RestoreOriginalText();
    }

    [HideFromIl2Cpp]
    public void Refresh()
    {
        if (_isOverrideActive)
        {
            TryResolveLabel();
            ApplyOverride();
        }
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

        if (_label == null)
            return false;

        if (!_hasOriginalText)
        {
            _originalText = _label.text;
            _hasOriginalText = true;
        }

        return true;
    }

    [HideFromIl2Cpp]
    private void ApplyOverride()
    {
        if (_isOverrideActive && _label != null && _label.text != _overrideText)
            _label.text = _overrideText;
    }

    [HideFromIl2Cpp]
    private void RestoreOriginalText()
    {
        if (_label == null || !_hasOriginalText)
            return;

        _label.text = _originalText;
    }
}

public static class AuthStatusOverlay
{
    public const string AuthenticatingMessage = "Authenticating, Please Wait...";

    public static void BeginAuthenticating()
    {
        var component = EnsureComponent();
        component?.BeginOverride(AuthenticatingMessage);
    }

    public static void EndAuthenticating(bool restoreOriginalText = true)
    {
        var component = GetComponent();
        component?.EndOverride(restoreOriginalText);
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
