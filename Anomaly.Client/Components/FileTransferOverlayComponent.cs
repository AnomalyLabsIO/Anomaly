using Anomaly.Client.API.Localization;
using Anomaly.Client.Internal.Localization;
using Il2CppInterop.Runtime.Attributes;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace Anomaly.Client.Components;

[RegisterTypeInIl2Cpp]
public class FileTransferOverlayComponent : MonoBehaviour
{
    private TMP_Text _statusText;
    private TMP_Text _percentText;
    private Image _barFill;
    private GameObject _root;

    private string _pendingStatus;
    private string _pendingPercent;
    private float _pendingFill;
    private bool _dirty;

    public FileTransferOverlayComponent(IntPtr intPtr) : base(intPtr) { }
    public FileTransferOverlayComponent() { }

    private void Awake()
    {
        BuildUI();
        _root.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!_dirty) return;
        _dirty = false;

        if (_statusText != null) _statusText.text = _pendingStatus ?? "";
        if (_percentText != null) _percentText.text = _pendingPercent ?? "";
        if (_barFill != null) _barFill.fillAmount = _pendingFill;
    }

    [HideFromIl2Cpp]
    public void Show()
    {
        if (_root != null) _root.SetActive(true);
    }

    [HideFromIl2Cpp]
    public void Hide()
    {
        if (_root != null) _root.SetActive(false);
    }

    [HideFromIl2Cpp]
    public void UpdateProgress(string fileName, int fileIndex, int totalFiles, float percent)
    {
        _pendingStatus = Tr.Get(TrKeys.FileTransfer_Downloading, fileName, fileIndex, totalFiles);
        _pendingPercent = $"{Mathf.RoundToInt(percent * 100f)}%";
        _pendingFill = Mathf.Clamp01(percent);
        _dirty = true;
    }

    [HideFromIl2Cpp]
    private void BuildUI()
    {
        // Root canvas
        var canvasGo = new GameObject("FileTransferCanvas");
        canvasGo.transform.SetParent(transform);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        _root = canvasGo;

        // Panel background (bottom-center)
        var panelGo = CreateUIElement("Panel", canvasGo.transform);
        var panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 20f);
        panelRect.sizeDelta = new Vector2(500f, 70f);
        var panelImg = panelGo.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.75f);

        // Status text (top of panel)
        var statusGo = CreateUIElement("StatusText", panelGo.transform);
        var statusRect = statusGo.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0f, 0.5f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.offsetMin = new Vector2(12f, 0f);
        statusRect.offsetMax = new Vector2(-12f, -4f);
        _statusText = statusGo.AddComponent<TextMeshProUGUI>();
        _statusText.fontSize = 14f;
        _statusText.color = Color.white;
        _statusText.alignment = TextAlignmentOptions.Left;
        _statusText.text = Tr.Get(TrKeys.FileTransfer_Preparing);

        // Bar track (bottom half of panel)
        var trackGo = CreateUIElement("BarTrack", panelGo.transform);
        var trackRect = trackGo.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(0.85f, 0.45f);
        trackRect.offsetMin = new Vector2(12f, 8f);
        trackRect.offsetMax = new Vector2(0f, 0f);
        var trackImg = trackGo.AddComponent<Image>();
        trackImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        // Bar fill (child of track)
        var fillGo = CreateUIElement("BarFill", trackGo.transform);
        var fillRect = fillGo.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        _barFill = fillGo.AddComponent<Image>();
        _barFill.color = new Color(0.2f, 0.7f, 1f, 1f);
        _barFill.type = Image.Type.Filled;
        _barFill.fillMethod = Image.FillMethod.Horizontal;
        _barFill.fillAmount = 0f;

        // Percent text (right side)
        var pctGo = CreateUIElement("PercentText", panelGo.transform);
        var pctRect = pctGo.GetComponent<RectTransform>();
        pctRect.anchorMin = new Vector2(0.85f, 0f);
        pctRect.anchorMax = new Vector2(1f, 0.45f);
        pctRect.offsetMin = new Vector2(4f, 8f);
        pctRect.offsetMax = new Vector2(-12f, 0f);
        _percentText = pctGo.AddComponent<TextMeshProUGUI>();
        _percentText.fontSize = 16f;
        _percentText.color = Color.white;
        _percentText.alignment = TextAlignmentOptions.Center;
        _percentText.text = "0%";
    }

    private static GameObject CreateUIElement(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }
}

public static class FileTransferOverlay
{
    private static FileTransferOverlayComponent _instance;

    public static void Show()
    {
        EnsureInstance();
        _instance?.Show();
    }

    public static void UpdateProgress(string fileName, int fileIndex, int totalFiles, float percent)
    {
        _instance?.UpdateProgress(fileName, fileIndex, totalFiles, percent);
    }

    public static void Hide()
    {
        _instance?.Hide();
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;

        var go = new GameObject("AnomalyFileTransferOverlay");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<FileTransferOverlayComponent>();
    }
}
