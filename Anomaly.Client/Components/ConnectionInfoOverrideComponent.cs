using Anomaly.Client.Api.Features;
using Il2Cpp;
using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using ClientFeature = Anomaly.Client.Api.Features.Client;

namespace Anomaly.Client.Components;

[RegisterTypeInIl2Cpp]
public class ConnectionInfoOverrideComponent : MonoBehaviour
{
    private const string ReasonContentPath = "ConnectionInfo/Background/Container/ReasonContent";

    // ReasonContent uses legacy UnityEngine.UI.Text (not TMP) — vanilla scene
    // ships with `<size=80>DISCONNECTED</size>...` showing the rich-text format.
    private Text _label;
    private bool _resolveWarned;

    private bool _isOverrideActive;
    private string _overrideText;
    private bool _originalRichText;
    private bool _hasOriginalRichText;

    public ConnectionInfoOverrideComponent(IntPtr intPtr) : base(intPtr)
    {
    }

    public ConnectionInfoOverrideComponent()
    {
    }

    private void LateUpdate()
    {
        if (!_isOverrideActive)
            return;

        if (!TryResolveLabel())
            return;

        ApplyCurrent();
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
        if (_label != null && _hasOriginalRichText) _label.supportRichText = _originalRichText;
        _hasOriginalRichText = false;
    }

    [HideFromIl2Cpp]
    private bool TryResolveLabel()
    {
        if (_label != null)
            return true;

        var manager = CustomNetworkManager.TypedSingleton;
        if (manager == null)
            return false;

        var labelTransform = manager.transform.Find(ReasonContentPath);
        if (labelTransform == null)
        {
            if (!_resolveWarned)
            {
                MelonLogger.Warning($"ConnectionInfoOverlay: ReasonContent not found at '{ReasonContentPath}' under NetworkManager.");
                _resolveWarned = true;
            }
            return false;
        }

        _label = labelTransform.GetComponent<Text>();
        if (_label == null && !_resolveWarned)
        {
            MelonLogger.Warning($"ConnectionInfoOverlay: ReasonContent missing UnityEngine.UI.Text component.");
            _resolveWarned = true;
        }
        return _label != null;
    }

    [HideFromIl2Cpp]
    private void ApplyCurrent()
    {
        if (_label == null || !_isOverrideActive)
            return;

        if (!_hasOriginalRichText)
        {
            _originalRichText = _label.supportRichText;
            _hasOriginalRichText = true;
        }
        _label.supportRichText = false;

        if (_label.text != _overrideText)
            _label.text = _overrideText;
    }
}

public static class ConnectionInfoOverlay
{
    private const string FailurePromptOwner = "connection-failure";
    private const string AuthSpinnerMenuPath = "Servers/Auth Status/Loading";
    private const string ConnectionInfoContainerPath = "ConnectionInfo/Background/Container";

    private static Transform _spinner;
    private static bool _spinnerWarned;
    private static bool _targetWarned;

    public static void RelocateSpinner()
    {
        try
        {
            var menu = ClientFeature.MainMenu;
            if (menu == null)
                return;

            // Unity's null overload returns true on destroyed objects, so this
            // check naturally re-relocates if the cached spinner gets killed.
            bool haveRelocated = _spinner != null
                && _spinner.parent != null
                && _spinner.parent.name == "Container";

            var spinnerInMenu = menu.transform.Find(AuthSpinnerMenuPath);

            // On scene reload, the new NewMainMenu instantiates a fresh
            // Servers/Auth Status/Loading from the scene template. We already
            // have the relocated one under Container — kill the duplicate so
            // the user doesn't see two spinners.
            if (haveRelocated)
            {
                if (spinnerInMenu != null)
                    UnityEngine.Object.Destroy(spinnerInMenu.gameObject);
                return;
            }

            if (spinnerInMenu == null)
            {
                if (!_spinnerWarned)
                {
                    MelonLogger.Warning($"ConnectionInfoOverlay: spinner not found at '{AuthSpinnerMenuPath}'.");
                    _spinnerWarned = true;
                }
                return;
            }

            var manager = CustomNetworkManager.TypedSingleton;
            if (manager == null)
            {
                if (!_targetWarned)
                {
                    MelonLogger.Warning("ConnectionInfoOverlay: CustomNetworkManager.TypedSingleton is null.");
                    _targetWarned = true;
                }
                return;
            }

            var container = manager.transform.Find(ConnectionInfoContainerPath);
            if (container == null)
            {
                if (!_targetWarned)
                {
                    MelonLogger.Warning($"ConnectionInfoOverlay: target container not found at '{ConnectionInfoContainerPath}'.");
                    _targetWarned = true;
                }
                return;
            }

            spinnerInMenu.SetParent(container, worldPositionStays: false);

            // Reposition: vanilla anchors put it top-left (offset 35,-35 from
            // Auth Status). The override popup centers ReasonContent in the
            // container, so center-above the title reads as a progress
            // indicator paired with the "Authenticating..." text.
            if (spinnerInMenu is RectTransform rect)
            {
                var center = new Vector2(0.5f, 0.5f);
                rect.anchorMin = center;
                rect.anchorMax = center;
                rect.pivot = center;
                rect.anchoredPosition = new Vector2(0f, 90f);
            }

            spinnerInMenu.gameObject.SetActive(false);
            _spinner = spinnerInMenu;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ConnectionInfoOverlay: relocate spinner failed: {ex.Message}");
        }
    }

    public static void BeginOverride(string text)
    {
        var component = EnsureComponent();
        component?.BeginOverride(text);
        SetSpinnerActive(true);
    }

    public static void EndOverride()
    {
        var component = GetComponent();
        component?.EndOverride();
        SetSpinnerActive(false);
    }

    internal static void ShowFailure(string reason, string action, string code)
    {
        EndOverride();
        // Authentication teardown only hides its own prompt. This terminal notice
        // survives subsequent disconnect notifications until dismissed or replaced.
        PasswordPrompt.ShowNotice(FailurePromptOwner, "Connection could not be completed",
            reason + "\n\n" + action + "\n\nDiagnostic code: " + code
                + "\nRun anomaly.status in the game console for diagnostic locations.", EndOverride);
    }

    private static void SetSpinnerActive(bool active)
    {
        if (_spinner == null)
            RelocateSpinner();

        if (_spinner == null)
            return;

        var go = _spinner.gameObject;
        if (go.activeSelf != active)
            go.SetActive(active);
    }

    private static ConnectionInfoOverrideComponent GetComponent()
    {
        var menu = ClientFeature.MainMenu;
        return menu == null ? null : menu.GetComponent<ConnectionInfoOverrideComponent>();
    }

    private static ConnectionInfoOverrideComponent EnsureComponent()
    {
        var menu = ClientFeature.MainMenu;
        if (menu == null)
            return null;

        var component = menu.GetComponent<ConnectionInfoOverrideComponent>();
        return component ?? menu.gameObject.AddComponent<ConnectionInfoOverrideComponent>();
    }
}
