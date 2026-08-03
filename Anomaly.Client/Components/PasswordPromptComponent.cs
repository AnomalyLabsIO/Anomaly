using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Il2CppInterop.Runtime.Attributes;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace Anomaly.Client.Components;

[RegisterTypeInIl2Cpp]
public class PasswordPromptComponent : MonoBehaviour
{
    private TMP_Text _titleText;
    private TMP_Text _passwordText;
    private TMP_Text _hintText;
    private GameObject _root;
    private string _password = string.Empty;
    private System.Action<string> _onSubmit;
    private System.Action _onConfirm;
    private System.Action _onCancel;
    private bool _confirmationMode;
    private bool _plainTextMode;
    private bool _wasCursorVisible;

    public PasswordPromptComponent(IntPtr intPtr) : base(intPtr) { }
    public PasswordPromptComponent() { }

    private void Awake()
    {
        BuildUI();
        _root.SetActive(false);
    }

    private void Update()
    {
        if (_root == null || !_root.activeSelf)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cancel();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            Submit();
            return;
        }

        if (_confirmationMode)
            return;

        if (TryPasteFromClipboard())
        {
            UpdatePasswordText();
            return;
        }

        foreach (var c in Input.inputString)
        {
            if (c == '\b')
            {
                if (_password.Length > 0)
                    _password = _password.Substring(0, _password.Length - 1);
                continue;
            }

            AppendPasswordChar(c);
        }

        UpdatePasswordText();
    }

    [HideFromIl2Cpp]
    private bool TryPasteFromClipboard()
    {
        var commandModifier = Input.GetKey(KeyCode.LeftControl)
            || Input.GetKey(KeyCode.RightControl)
            || Input.GetKey(KeyCode.LeftCommand)
            || Input.GetKey(KeyCode.RightCommand);
        var shiftModifier = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        var pastePressed = (commandModifier && Input.GetKeyDown(KeyCode.V))
            || (shiftModifier && Input.GetKeyDown(KeyCode.Insert));

        if (!pastePressed)
            return false;

        var clipboard = GUIUtility.systemCopyBuffer;
        if (string.IsNullOrEmpty(clipboard))
            return true;

        foreach (var c in clipboard)
            AppendPasswordChar(c);

        return true;
    }

    [HideFromIl2Cpp]
    private void AppendPasswordChar(char c)
    {
        if (c == '\n' || c == '\r' || char.IsControl(c))
            return;

        _password += c;
    }

    [HideFromIl2Cpp]
    public void Show(System.Action<string> onSubmit, System.Action onCancel)
    {
        _confirmationMode = false;
        _plainTextMode = false;
        _password = string.Empty;
        _onSubmit = onSubmit;
        _onConfirm = null;
        _onCancel = onCancel;
        _wasCursorVisible = Cursor.visible;
        Cursor.visible = true;

        _titleText.text = Tr.Get(TrKeys.PasswordPrompt_Title);
        _hintText.text = Tr.Get(TrKeys.PasswordPrompt_Hint);
        _passwordText.alignment = TextAlignmentOptions.MidlineLeft;
        UpdatePasswordText();
        _root.SetActive(true);
    }

    [HideFromIl2Cpp]
    public void ShowConfirmation(
        string title,
        string detail,
        System.Action onConfirm,
        System.Action onCancel)
    {
        _confirmationMode = true;
        _plainTextMode = false;
        _password = detail ?? string.Empty;
        _onSubmit = null;
        _onConfirm = onConfirm;
        _onCancel = onCancel;
        _wasCursorVisible = Cursor.visible;
        Cursor.visible = true;

        _titleText.text = title ?? "Confirm";
        _hintText.text = "Press Enter to trust this server key. Press Esc to cancel.";
        _passwordText.alignment = TextAlignmentOptions.Center;
        UpdatePasswordText();
        _root.SetActive(true);
    }

    [HideFromIl2Cpp]
    public void ShowTextInput(
        string title,
        string hint,
        System.Action<string> onSubmit,
        System.Action onCancel)
    {
        _confirmationMode = false;
        _plainTextMode = true;
        _password = string.Empty;
        _onSubmit = onSubmit;
        _onConfirm = null;
        _onCancel = onCancel;
        _wasCursorVisible = Cursor.visible;
        Cursor.visible = true;

        _titleText.text = title ?? "Enter text";
        _hintText.text = hint ?? "Press Enter to continue. Press Esc to cancel.";
        _passwordText.alignment = TextAlignmentOptions.MidlineLeft;
        UpdatePasswordText();
        _root.SetActive(true);
    }

    [HideFromIl2Cpp]
    public void Hide()
    {
        if (_root != null)
            _root.SetActive(false);

        Cursor.visible = _wasCursorVisible;
        _password = string.Empty;
        _onSubmit = null;
        _onConfirm = null;
        _onCancel = null;
        _confirmationMode = false;
        _plainTextMode = false;
        UpdatePasswordText();
    }

    [HideFromIl2Cpp]
    private void Submit()
    {
        var password = _password;
        var callback = _onSubmit;
        var confirm = _onConfirm;
        var confirmationMode = _confirmationMode;
        Hide();

        try
        {
            if (confirmationMode)
                confirm?.Invoke();
            else
                callback?.Invoke(password);
        }
        catch (Exception ex) { MelonLogger.Error($"Password prompt submit failed: {ex}"); }
    }

    [HideFromIl2Cpp]
    private void Cancel()
    {
        var callback = _onCancel;
        Hide();

        try { callback?.Invoke(); }
        catch (Exception ex) { MelonLogger.Error($"Password prompt cancel failed: {ex}"); }
    }

    [HideFromIl2Cpp]
    private void UpdatePasswordText()
    {
        if (_passwordText == null)
            return;

        _passwordText.text = _confirmationMode
            ? _password
            : (_password.Length == 0
                ? "_"
                : (_plainTextMode ? _password : new string('*', _password.Length)));
    }

    [HideFromIl2Cpp]
    private void BuildUI()
    {
        var canvasGo = new GameObject("PasswordPromptCanvas");
        canvasGo.transform.SetParent(transform);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        _root = canvasGo;

        var dimGo = CreateUIElement("Dim", canvasGo.transform);
        var dimRect = dimGo.GetComponent<RectTransform>();
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = Vector2.zero;
        dimRect.offsetMax = Vector2.zero;
        var dim = dimGo.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);

        var panelGo = CreateUIElement("Panel", canvasGo.transform);
        var panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(560f, 240f);
        panelRect.anchoredPosition = Vector2.zero;
        var panel = panelGo.AddComponent<Image>();
        panel.color = new Color(0.03f, 0.035f, 0.045f, 0.96f);

        var titleGo = CreateUIElement("Title", panelGo.transform);
        var titleRect = titleGo.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(30f, -72f);
        titleRect.offsetMax = new Vector2(-30f, -24f);
        _titleText = titleGo.AddComponent<TextMeshProUGUI>();
        _titleText.fontSize = 30f;
        _titleText.color = Color.white;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.text = Tr.Get(TrKeys.PasswordPrompt_Title);

        var inputGo = CreateUIElement("Password", panelGo.transform);
        var inputRect = inputGo.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0f, 0.5f);
        inputRect.anchorMax = new Vector2(1f, 0.5f);
        inputRect.pivot = new Vector2(0.5f, 0.5f);
        inputRect.offsetMin = new Vector2(70f, -32f);
        inputRect.offsetMax = new Vector2(-70f, 24f);
        var inputBg = inputGo.AddComponent<Image>();
        inputBg.color = new Color(0.11f, 0.12f, 0.15f, 1f);

        var inputTextGo = CreateUIElement("Text", inputGo.transform);
        var inputTextRect = inputTextGo.GetComponent<RectTransform>();
        inputTextRect.anchorMin = Vector2.zero;
        inputTextRect.anchorMax = Vector2.one;
        inputTextRect.offsetMin = new Vector2(18f, 5f);
        inputTextRect.offsetMax = new Vector2(-18f, -5f);
        _passwordText = inputTextGo.AddComponent<TextMeshProUGUI>();
        _passwordText.fontSize = 26f;
        _passwordText.color = Color.white;
        _passwordText.alignment = TextAlignmentOptions.MidlineLeft;

        var hintGo = CreateUIElement("Hint", panelGo.transform);
        var hintRect = hintGo.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.offsetMin = new Vector2(30f, 28f);
        hintRect.offsetMax = new Vector2(-30f, 74f);
        _hintText = hintGo.AddComponent<TextMeshProUGUI>();
        _hintText.fontSize = 18f;
        _hintText.color = new Color(0.78f, 0.82f, 0.9f, 1f);
        _hintText.alignment = TextAlignmentOptions.Center;
        _hintText.text = Tr.Get(TrKeys.PasswordPrompt_Hint);
    }

    private static GameObject CreateUIElement(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }
}

public static class PasswordPrompt
{
    private static PasswordPromptComponent _instance;

    public static void Show(System.Action<string> onSubmit, System.Action onCancel)
    {
        EnsureInstance();
        _instance?.Show(onSubmit, onCancel);
    }

    public static void ShowConfirmation(
        string title,
        string detail,
        System.Action onConfirm,
        System.Action onCancel)
    {
        EnsureInstance();
        _instance?.ShowConfirmation(title, detail, onConfirm, onCancel);
    }

    public static void ShowTextInput(
        string title,
        string hint,
        System.Action<string> onSubmit,
        System.Action onCancel)
    {
        EnsureInstance();
        _instance?.ShowTextInput(title, hint, onSubmit, onCancel);
    }

    public static void Hide()
    {
        _instance?.Hide();
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;

        var go = new GameObject("AnomalyPasswordPrompt");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<PasswordPromptComponent>();
    }
}
