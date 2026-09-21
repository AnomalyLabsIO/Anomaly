using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Internal.UI;
using Il2CppCursorManagement;
using Il2CppInterop.Runtime.Attributes;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Anomaly.Client.Components;

/// <summary>Internal modal shell shared by authentication and content consent.</summary>
[RegisterTypeInIl2CppWithInterfaces(typeof(ICursorOverride))]
public class PasswordPromptComponent : MonoBehaviour
{
    private GameObject _root;
    private RectTransform _safeArea;
    private RectTransform _panel;
    private TMP_Text _title;
    private TMP_Text _details;
    private TMP_Text _error;
    private TMP_InputField _input;
    private ScrollRect _scroll;
    private Button _accept;
    private Button _cancel;
    private TMP_Text _acceptLabel;
    private TMP_Text _cancelLabel;
    private System.Action<string> _onSubmit;
    private System.Func<string, string> _validateAndSubmit;
    private System.Action _onCancel;
    private ICursorOverride _cursorOverride;
    private GameObject _previousSelection;
    private Selectable _initialSelection;
    private bool _focusPending;
    private bool _isVisible;
    private long _presentation;

    public PasswordPromptComponent(IntPtr pointer) : base(pointer) { }
    public PasswordPromptComponent() { }

    // These accessors implement the game's injected ICursorOverride interface.
    public CursorOverrideMode CursorOverride => _isVisible ? CursorOverrideMode.Free : CursorOverrideMode.NoOverride;
    public bool LockMovement => _isVisible;

    private void Awake()
    {
        BuildUI();
        _root.SetActive(false);
    }

    private void Update()
    {
        if (_root == null || !_root.activeSelf) return;
        UpdateSafeArea();
        var events = EventSystem.current;
        var selected = events?.currentSelectedGameObject;
        if (events != null && (_focusPending || selected == null || !selected.activeInHierarchy
            || !selected.transform.IsChildOf(_root.transform)))
        {
            Select(_initialSelection);
            _focusPending = false;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cancel();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Tab))
            CycleFocus(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        if (Input.GetKeyDown(KeyCode.PageDown))
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition - 0.25f);
        if (Input.GetKeyDown(KeyCode.PageUp))
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + 0.25f);
    }

    private void OnDestroy() => Hide();
    private void OnDisable() => Hide();

    [HideFromIl2Cpp]
    internal void Show(string title, string details, string acceptLabel, string cancelLabel,
        bool hasInput, bool password, string initialValue, System.Action<string> onSubmit,
        System.Action onCancel, System.Func<string, string> validateAndSubmit = null, bool singleAction = false)
    {
        if (!_isVisible)
        {
            _previousSelection = EventSystem.current?.currentSelectedGameObject;
        }
        _presentation++;
        _isVisible = true;
        _onSubmit = onSubmit;
        _validateAndSubmit = validateAndSubmit;
        _onCancel = onCancel;
        _title.text = title ?? string.Empty;
        _details.text = details ?? string.Empty;
        _error.text = string.Empty;
        _acceptLabel.text = acceptLabel;
        _cancelLabel.text = cancelLabel;
        _accept.gameObject.SetActive(!singleAction);
        var cancelRect = _cancel.GetComponent<RectTransform>();
        cancelRect.anchorMax = new Vector2(singleAction ? 1f : 0.5f, 0f);
        cancelRect.offsetMax = new Vector2(singleAction ? -34f : -10f, 84f);
        var cancelNavigation = _cancel.navigation;
        cancelNavigation.selectOnLeft = singleAction ? _cancel : _accept;
        cancelNavigation.selectOnRight = singleAction ? _cancel : _accept;
        _cancel.navigation = cancelNavigation;
        _input.gameObject.SetActive(hasInput);
        Stretch(_scroll.GetComponent<RectTransform>(), 34, hasInput ? 228 : 112, 34, 110);
        _input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
        _input.characterLimit = password ? 0 : 128;
        _input.SetTextWithoutNotify(initialValue ?? string.Empty);
        _initialSelection = hasInput ? _input : _cancel;
        _root.SetActive(true);
        _focusPending = true;
        _cursorOverride ??= this.Cast<ICursorOverride>();
        CursorManager.Register(_cursorOverride);
        CursorManager.LateUpdate();
        UpdateSafeArea();
        Canvas.ForceUpdateCanvases();
        _scroll.verticalNormalizedPosition = 1f;
    }

    [HideFromIl2Cpp]
    internal void Hide()
    {
        if (!_isVisible) return;
        _isVisible = false;
        _presentation++;
        if (_root != null) _root.SetActive(false);
        if (_input != null) _input.SetTextWithoutNotify(string.Empty);
        _onSubmit = null;
        _validateAndSubmit = null;
        _onCancel = null;
        _focusPending = false;
        if (_cursorOverride != null) CursorManager.Unregister(_cursorOverride);
        // Recompute from remaining owners so a menu opened during this prompt is
        // preserved, and gameplay movement/cursor state returns immediately.
        CursorManager.LateUpdate();
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_previousSelection != null && _previousSelection.activeInHierarchy
                ? _previousSelection : null);
        _previousSelection = null;
    }

    [HideFromIl2Cpp]
    private void Submit()
    {
        if (!_root.activeSelf) return;
        var callback = _onSubmit;
        string value = _input.text;
        if (_validateAndSubmit != null)
        {
            long presentation = _presentation;
            string error;
            try { error = _validateAndSubmit(value); }
            catch (Exception exception)
            {
                MelonLogger.Error($"Anomaly prompt submit failed: {exception}");
                error = Tr.Get(TrKeys.Prompt_SaveError);
            }
            // The callback may replace or cancel this prompt. Never close its successor.
            if (_presentation != presentation) return;
            if (!string.IsNullOrEmpty(error))
            {
                _error.text = error;
                Select(_input);
                return;
            }
            Hide();
            return;
        }
        Hide();
        try { callback?.Invoke(value); }
        catch (Exception exception) { MelonLogger.Error($"Anomaly prompt submit failed: {exception}"); }
    }

    [HideFromIl2Cpp]
    private void Cancel()
    {
        var callback = _onCancel;
        Hide();
        try { callback?.Invoke(); }
        catch (Exception exception) { MelonLogger.Error($"Anomaly prompt cancel failed: {exception}"); }
    }

    [HideFromIl2Cpp]
    private void CycleFocus(bool backwards)
    {
        var choices = !_accept.gameObject.activeSelf ? new Selectable[] { _cancel } : _input.gameObject.activeSelf
            ? new Selectable[] { _input, _cancel, _accept }
            : new Selectable[] { _cancel, _accept };
        var selected = EventSystem.current?.currentSelectedGameObject;
        int index = Array.FindIndex(choices, item => item.gameObject == selected);
        int next = index < 0 ? 0 : (index + (backwards ? choices.Length - 1 : 1)) % choices.Length;
        Select(choices[next]);
    }

    [HideFromIl2Cpp]
    private void Select(Selectable selectable)
    {
        if (selectable == null || EventSystem.current == null) return;
        EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        if (selectable == _input) _input.ActivateInputField();
    }

    [HideFromIl2Cpp]
    private void UpdateSafeArea()
    {
        if (Screen.width <= 0 || Screen.height <= 0) return;
        var safe = Screen.safeArea;
        _safeArea.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        _safeArea.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        var canvasRect = _root.GetComponent<RectTransform>().rect;
        float width = canvasRect.width * safe.width / Screen.width;
        float height = canvasRect.height * safe.height / Screen.height;
        _panel.sizeDelta = new Vector2(Mathf.Min(820f, width - 40f), Mathf.Min(640f, height - 40f));
    }

    [HideFromIl2Cpp]
    private void BuildUI()
    {
        _root = Ui("AnomalyModalCanvas", transform);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<GraphicRaycaster>();
        var dim = Ui("Dim", _root.transform);
        Stretch(dim.GetComponent<RectTransform>(), 0, 0, 0, 0);
        dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);
        _safeArea = Ui("SafeArea", _root.transform).GetComponent<RectTransform>();
        Stretch(_safeArea, 0, 0, 0, 0);
        _panel = Ui("Panel", _safeArea).GetComponent<RectTransform>();
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
        _panel.gameObject.AddComponent<Image>().color = AnomalyUiStyle.Panel;
        var accent = Ui("Accent", _panel);
        var accentRect = accent.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0, 1);
        accentRect.anchorMax = Vector2.one;
        accentRect.offsetMin = new Vector2(0, -4);
        accentRect.offsetMax = Vector2.zero;
        accent.AddComponent<Image>().color = AnomalyUiStyle.Accent;
        _title = Text("Title", _panel, 30f);
        Top(_title.rectTransform, 28f, 34f, 72f);

        var scrollGo = Ui("Details", _panel);
        Stretch(scrollGo.GetComponent<RectTransform>(), 34, 228, 34, 110);
        _scroll = scrollGo.AddComponent<ScrollRect>();
        _scroll.horizontal = false;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 32f;
        var viewport = Ui("Viewport", scrollGo.transform);
        Stretch(viewport.GetComponent<RectTransform>(), 0, 0, 0, 0);
        viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
        viewport.AddComponent<RectMask2D>();
        _details = Text("Text", viewport.transform, 24f);
        var detailsRect = _details.rectTransform;
        detailsRect.anchorMin = new Vector2(0, 1);
        detailsRect.anchorMax = Vector2.one;
        detailsRect.pivot = new Vector2(0.5f, 1);
        detailsRect.sizeDelta = Vector2.zero;
        var fitter = _details.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scroll.viewport = viewport.GetComponent<RectTransform>();
        _scroll.content = detailsRect;

        var inputGo = Ui("Input", _panel);
        Bottom(inputGo.GetComponent<RectTransform>(), 170f, 34f, 52f);
        inputGo.AddComponent<Image>().color = AnomalyUiStyle.Input;
        _input = inputGo.AddComponent<TMP_InputField>();
        _input.lineType = TMP_InputField.LineType.SingleLine;
        AnomalyUiStyle.ApplySelectable(_input);
        var textArea = Ui("TextArea", inputGo.transform);
        Stretch(textArea.GetComponent<RectTransform>(), 14, 6, 14, 6);
        textArea.AddComponent<RectMask2D>();
        var text = Text("Value", textArea.transform, 25f);
        Stretch(text.rectTransform, 0, 0, 0, 0);
        text.enableWordWrapping = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        _input.textViewport = textArea.GetComponent<RectTransform>();
        _input.textComponent = text;
        _input.onSubmit.AddListener((UnityEngine.Events.UnityAction<string>)(_ => Submit()));
        _error = Text("Error", _panel, 20f);
        _error.color = AnomalyUiStyle.Error;
        Bottom(_error.rectTransform, 94f, 34f, 70f);

        _cancel = Button("Cancel", _panel, false, out _cancelLabel);
        _accept = Button("Accept", _panel, true, out _acceptLabel);
        _cancel.onClick.AddListener((UnityEngine.Events.UnityAction)Cancel);
        _accept.onClick.AddListener((UnityEngine.Events.UnityAction)Submit);
        var cancelNav = _cancel.navigation;
        cancelNav.mode = Navigation.Mode.Explicit;
        cancelNav.selectOnRight = _accept;
        cancelNav.selectOnLeft = _accept;
        _cancel.navigation = cancelNav;
        var acceptNav = _accept.navigation;
        acceptNav.mode = Navigation.Mode.Explicit;
        acceptNav.selectOnLeft = _cancel;
        acceptNav.selectOnRight = _cancel;
        _accept.navigation = acceptNav;
        var inputNav = _input.navigation;
        inputNav.mode = Navigation.Mode.Explicit;
        inputNav.selectOnDown = _cancel;
        inputNav.selectOnUp = _accept;
        _input.navigation = inputNav;
        var keyboardHint = Text("KeyboardHint", _panel, 15f);
        keyboardHint.text = Tr.Get(TrKeys.Prompt_KeyboardHint);
        keyboardHint.color = AnomalyUiStyle.Secondary;
        keyboardHint.alignment = TextAlignmentOptions.Center;
        Bottom(keyboardHint.rectTransform, 6f, 34f, 20f);
    }

    [HideFromIl2Cpp]
    private static GameObject Ui(string name, Transform parent)
    {
        var go = AnomalyUiStyle.MakeUi(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    [HideFromIl2Cpp]
    private static TMP_Text Text(string name, Transform parent, float size)
    {
        var text = Ui(name, parent).AddComponent<TextMeshProUGUI>();
        AnomalyUiStyle.ApplyText(text, size);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }

    [HideFromIl2Cpp]
    private static Button Button(string name, Transform parent, bool right, out TMP_Text label)
    {
        var go = Ui(name, parent);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(right ? 0.5f : 0f, 0);
        rect.anchorMax = new Vector2(right ? 1f : 0.5f, 0);
        rect.offsetMin = new Vector2(right ? 10 : 34, 30);
        rect.offsetMax = new Vector2(right ? -34 : -10, 84);
        go.AddComponent<Image>().color = AnomalyUiStyle.Input;
        var button = go.AddComponent<Button>();
        AnomalyUiStyle.ApplySelectable(button);
        label = Text("Label", go.transform, 23f);
        Stretch(label.rectTransform, 10, 4, 10, 4);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }

    [HideFromIl2Cpp]
    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    [HideFromIl2Cpp]
    private static void Top(RectTransform rect, float top, float inset, float height)
    {
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, -top - height);
        rect.offsetMax = new Vector2(-inset, -top);
    }

    [HideFromIl2Cpp]
    private static void Bottom(RectTransform rect, float bottom, float inset, float height)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1, 0);
        rect.offsetMin = new Vector2(inset, bottom);
        rect.offsetMax = new Vector2(-inset, bottom + height);
    }
}

internal static class PasswordPrompt
{
    private static PasswordPromptComponent _instance;
    private static readonly PromptLeaseGate LeaseGate = new();

    internal static void Show(string owner, System.Action<string> onSubmit, System.Action onCancel)
    {
        EnsureInstance();
        var lease = LeaseGate.Begin(owner);
        _instance.Show(Tr.Get(TrKeys.PasswordPrompt_Title), Tr.Get(TrKeys.PasswordPrompt_Hint),
            Tr.Get(TrKeys.Prompt_Connect), Tr.Get(TrKeys.Prompt_Cancel), true, true, string.Empty,
            value => { if (LeaseGate.TryConsume(lease)) onSubmit?.Invoke(value); },
            () => { if (LeaseGate.TryConsume(lease)) onCancel?.Invoke(); });
    }

    internal static void ShowConfirmation(string owner, string title, string detail,
        string acceptLabel, string cancelLabel, System.Action onConfirm, System.Action onCancel)
    {
        EnsureInstance();
        var lease = LeaseGate.Begin(owner);
        _instance.Show(title, detail, acceptLabel, cancelLabel, false, false, string.Empty,
            _ => { if (LeaseGate.TryConsume(lease)) onConfirm?.Invoke(); },
            () => { if (LeaseGate.TryConsume(lease)) onCancel?.Invoke(); });
    }

    internal static void ShowTextInput(string owner, string title, string detail, string initialValue,
        System.Func<string, string> validateAndSubmit, System.Action onCancel)
    {
        EnsureInstance();
        var lease = LeaseGate.Begin(owner);
        _instance.Show(title, detail, Tr.Get(TrKeys.Prompt_Save), Tr.Get(TrKeys.Prompt_Cancel), true, false,
            initialValue, null, () => { if (LeaseGate.TryConsume(lease)) onCancel?.Invoke(); },
            value =>
            {
                LeaseGate.TrySubmit(lease, () => validateAndSubmit(value), out string error);
                return error;
            });
    }

    internal static void ShowNotice(string owner, string title, string detail, System.Action onDismiss)
    {
        EnsureInstance();
        var lease = LeaseGate.Begin(owner);
        _instance.Show(title, detail, string.Empty, Tr.Get(TrKeys.Prompt_Dismiss), false, false,
            string.Empty, null, () => { if (LeaseGate.TryConsume(lease)) onDismiss?.Invoke(); },
            singleAction: true);
    }

    internal static void Hide(string owner)
    {
        if (LeaseGate.Invalidate(owner)) _instance?.Hide();
    }

    internal static void HideAll()
    {
        LeaseGate.InvalidateAll();
        _instance?.Hide();
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;
        var go = new GameObject("AnomalyModal");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<PasswordPromptComponent>();
    }
}
