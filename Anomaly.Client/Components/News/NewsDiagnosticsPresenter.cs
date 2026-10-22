using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Client.Diagnostics.News;
using Anomaly.Client.Internal.UI;
using Anomaly.Client.Internal.UI.News;
using Il2Cpp;
using Il2CppInterop.Runtime.Attributes;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Anomaly.Client.Components.News;

/// <summary>Scene-owned News presentation; reads detached diagnostics on the game thread.</summary>
[RegisterTypeInIl2Cpp]
public sealed class NewsDiagnosticsPresenter : MonoBehaviour
{
    internal static NewsDiagnosticsPresenter Active { get; private set; }
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly List<NativeControl> _nativeControls = new();
    private NewsLoader _loader;
    private RectTransform _root;
    private RectTransform _panel;
    private RectTransform _content;
    private ScrollRect _scroll;
    private TMP_Text _versions;
    private TMP_Text _title;
    private TMP_Text _help;
    private TMP_Text _socialHeader;
    private TMP_Text _status;
    private TMP_Text _copyLabel;
    private GameObject _social;
    private GridLayoutGroup _socialGrid;
    private GameObject _nativeHeader;
    private GameObject _nativeArticle;
    private GameObject _nativeThumbnail;
    private bool _articleWasActive;
    private bool _thumbnailWasActive;
    private bool _headerWasActive;
    private bool _committed;
    private bool _wide;
    private bool _stacked;
    private bool _dirty;
    private bool _hasRendered;
    private bool _hasDataShown;
    private bool _warned;
    private int _revision = -1;
    private float _width;
    private float _height;
    private double _nextRefresh;
    private double _copyReset;
    private GameObject _lastSelected;
    private NewsDiagnosticSnapshot _snapshot;
    private NewsDiagnosticSnapshot _smokeSnapshot;

    public NewsDiagnosticsPresenter(IntPtr pointer) : base(pointer) { }
    public NewsDiagnosticsPresenter() { }

    [HideFromIl2Cpp]
    internal void Initialize(NewsLoader loader)
    {
        _loader = loader;
        _root = GetComponent<RectTransform>();
        _root.anchorMin = new Vector2(0.025f, 0.115f);
        _root.anchorMax = new Vector2(0.975f, 0.875f);
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;
        _nativeArticle = loader.transform.Find("Article")?.gameObject;
        _nativeThumbnail = loader.transform.Find("Thumbnail")?.gameObject;
        var social = loader.transform.Find("Social Media");
        if (social != null)
        {
            _nativeHeader = social.Find("Header")?.gameObject;
            var tiles = new HashSet<int>();
            foreach (var nativeButton in social.GetComponentsInChildren<SocialMediaButton>(includeInactive: true))
            {
                // Stock links are nested Image controls inside named tile wrappers,
                // not buttons on the direct Social Media children. Move the entire
                // wrapper so click/hover/animation bindings remain together.
                var tile = nativeButton.transform;
                while (tile.parent != null && tile.parent != social) tile = tile.parent;
                if (tile.parent != social || tile.name == "Header" || tile.name == "Beta Achievements Menu") continue;
                var rect = tile.GetComponent<RectTransform>();
                if (rect != null && tiles.Add(tile.GetInstanceID()))
                    _nativeControls.Add(new NativeControl(rect, tile.GetSiblingIndex(), nativeButton.GetComponent<RectTransform>()));
            }
        }
        BuildShell();
        _snapshot = NewsDiagnosticService.Snapshot;
        Reflow();
        Render();
    }

    [HideFromIl2Cpp]
    internal void Commit()
    {
        _articleWasActive = _nativeArticle != null && _nativeArticle.activeSelf;
        _thumbnailWasActive = _nativeThumbnail != null && _nativeThumbnail.activeSelf;
        _headerWasActive = _nativeHeader != null && _nativeHeader.activeSelf;
        _committed = true;
        if (_nativeArticle != null) _nativeArticle.SetActive(false);
        if (_nativeThumbnail != null) _nativeThumbnail.SetActive(false);
        if (_nativeHeader != null) _nativeHeader.SetActive(false);
        foreach (var control in _nativeControls)
            control.MoveTo(_socialGrid.transform);
        Active = this;
        _dirty = true;
    }

    // The isolated in-process smoke harness uses reflection; never accept a fixture
    // through an ordinary client setting, public API, or networking message.
    [HideFromIl2Cpp]
    internal void SetSmokeSnapshot(NewsDiagnosticSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANOMALY_UI_SMOKE_DIR"))) return;
        _smokeSnapshot = snapshot;
        _snapshot = snapshot ?? NewsDiagnosticService.Snapshot;
        _dirty = true;
    }

    [HideFromIl2Cpp]
    internal void ClearSmokeSnapshot()
    {
        _smokeSnapshot = null;
        _snapshot = NewsDiagnosticService.Snapshot;
        _dirty = true;
    }

    private void Update()
    {
        if (!_committed || _root == null || _loader == null) return;
        try
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (_smokeSnapshot == null && now >= _nextRefresh)
            {
                _nextRefresh = now + 1;
                NewsDiagnosticService.Refresh();
                _snapshot = NewsDiagnosticService.Snapshot;
            }
            bool resized = Mathf.Abs(_root.rect.width - _width) > 0.5f || Mathf.Abs(_root.rect.height - _height) > 0.5f;
            if (resized) Reflow();
            if (_dirty || resized || _snapshot.Revision != _revision) Render();
            if (_copyReset != 0 && now >= _copyReset)
            {
                _copyReset = 0;
                _copyLabel.text = NewsDiagnosticText.Get("copy");
            }
            KeepSelectionVisible();
        }
        catch (Exception exception)
        {
            if (_warned) return;
            _warned = true;
            MelonLogger.Warning("News diagnostics refresh failed: " + exception.Message);
        }
    }

    [HideFromIl2Cpp]
    private void BuildShell()
    {
        var panel = Make("News.Panel", transform);
        _panel = panel.GetComponent<RectTransform>();
        panel.AddComponent<Image>().color = AnomalyUiStyle.Panel;
        Flow(panel, 10, 14);
        var header = Make("News.Header", panel.transform);
        Flow(header, 5, 0);
        _title = Text(header.transform, "News.Title", NewsDiagnosticText.Get("title"), 28, AnomalyUiStyle.Accent);
        _versions = Text(header.transform, "News.Versions", "", 18, AnomalyUiStyle.Secondary);
        _status = Text(header.transform, "News.Status", "", 20, AnomalyUiStyle.Text);

        var scrollGo = Make("News.ScrollView", panel.transform);
        var element = scrollGo.AddComponent<LayoutElement>();
        element.minHeight = 80;
        element.flexibleHeight = 1;
        scrollGo.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        _scroll = scrollGo.AddComponent<ScrollRect>();
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 32;
        var viewport = Make("News.Viewport", scrollGo.transform);
        Stretch(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<RectTransform>().offsetMax = new Vector2(-14, 0);
        viewport.AddComponent<RectMask2D>();
        var content = Make("News.Content", viewport.transform);
        _content = content.GetComponent<RectTransform>();
        _content.anchorMin = new Vector2(0, 1);
        _content.anchorMax = new Vector2(1, 1);
        _content.pivot = new Vector2(0.5f, 1);
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = Vector2.zero;
        Flow(content, 9, 2);
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scroll.viewport = viewport.GetComponent<RectTransform>();
        _scroll.content = _content;
        var scrollbarGo = Make("News.Scrollbar", scrollGo.transform);
        var scrollbarRect = scrollbarGo.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1, 0);
        scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.offsetMin = new Vector2(-8, 0);
        scrollbarRect.offsetMax = Vector2.zero;
        scrollbarGo.AddComponent<Image>().color = AnomalyUiStyle.Input;
        var handle = Make("News.Scrollbar.Handle", scrollbarGo.transform);
        Stretch(handle.GetComponent<RectTransform>());
        var handleImage = handle.AddComponent<Image>();
        handleImage.color = AnomalyUiStyle.Secondary;
        var scrollbar = scrollbarGo.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.GetComponent<RectTransform>();
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        _scroll.verticalScrollbar = scrollbar;
        _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        var footer = Make("News.Footer", panel.transform);
        Flow(footer, 5, 0);
        var copy = Button(footer.transform, "News.CopySummary", NewsDiagnosticText.Get("copy"), CopySummary);
        _copyLabel = copy.GetComponentInChildren<TMP_Text>();
        _help = Text(footer.transform, "News.Help", NewsDiagnosticText.Get("help"), 16, AnomalyUiStyle.Secondary);

        _social = Make("News.Social", transform);
        Flow(_social, 10, 8);
        _socialHeader = Text(_social.transform, "News.Social.Header", NewsDiagnosticText.Get("social"), 25, AnomalyUiStyle.Text);
        var grid = Make("News.Social.Grid", _social.transform);
        _socialGrid = grid.AddComponent<GridLayoutGroup>();
        _socialGrid.spacing = new Vector2(12, 12);
        _socialGrid.childAlignment = TextAnchor.UpperCenter;
        _socialGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.AddComponent<LayoutElement>();
    }

    [HideFromIl2Cpp]
    private void Reflow()
    {
        _width = _root.rect.width;
        _height = _root.rect.height;
        // Canvas-space dimensions follow the game's existing scaler. Aspect ratio
        // also matters: a 4:3 canvas can have the same reference width as 16:9.
        _wide = _width >= 1080 && _width / Math.Max(1, _height) >= 1.8f && _nativeControls.Count > 0;
        float sidebar = _wide ? Mathf.Clamp(_width * 0.25f, 280, 380) : 0;
        Stretch(_panel);
        _panel.offsetMax = new Vector2(_wide ? -sidebar - 16 : 0, 0);
        _stacked = _width - sidebar - (_wide ? 16 : 0) < 780;
        _dirty = true;
    }

    [HideFromIl2Cpp]
    private void Render()
    {
        bool hasData = _snapshot.Components.Length > 0 || _snapshot.CurrentIssues.Length > 0 || _snapshot.History.Length > 0;
        // An inactive empty shell has no meaningful normalized scroll position.
        // Its default zero must not place the first real snapshot at the bottom.
        float scrollPosition = !_hasRendered || hasData && !_hasDataShown ? 1 : _scroll.verticalNormalizedPosition;
        string selected = null;
        var events = EventSystem.current;
        if (events?.currentSelectedGameObject != null && events.currentSelectedGameObject.transform.IsChildOf(transform))
            selected = events.currentSelectedGameObject.name;
        // Native controls live outside the disposable content while rows rebuild.
        _social.transform.SetParent(transform, false);
        _social.SetActive(false);
        for (int index = _content.childCount - 1; index >= 0; index--)
        {
            var child = _content.GetChild(index).gameObject;
            child.SetActive(false);
            Object.Destroy(child);
        }
        _title.text = NewsDiagnosticText.Get("title");
        _help.text = NewsDiagnosticText.Get("help");
        _socialHeader.text = NewsDiagnosticText.Get("social");
        if (_copyReset == 0) _copyLabel.text = NewsDiagnosticText.Get("copy");
        _versions.text = NewsDiagnosticText.Get("versions", Known(_snapshot.GameVersion), Known(_snapshot.AnomalyVersion), Known(_snapshot.LoaderVersion));
        _status.text = _snapshot.IsCollecting ? NewsDiagnosticText.Get("collecting") : NewsDiagnosticText.Get("ready",
            _snapshot.Components.Count(component => component.State == DiagnosticLoadState.Loaded || component.State == DiagnosticLoadState.Registered),
            _snapshot.Components.Count(component => component.State == DiagnosticLoadState.Failed),
            _snapshot.Components.Count(component => component.State == DiagnosticLoadState.Disabled));
        var issues = Section("Issues", NewsDiagnosticText.Get("issues", _snapshot.CurrentIssues.Length), false);
        if (_snapshot.CurrentIssues.Length == 0)
            Text(issues, "News.Issues.Empty", NewsDiagnosticText.Get("clear"), 19, AnomalyUiStyle.Secondary);
        foreach (var issue in _snapshot.CurrentIssues) BuildIssue(issues, issue);
        var mods = _snapshot.Components.Where(component => component.Kind == DiagnosticComponentKind.Mod || component.Kind == DiagnosticComponentKind.ManagedModule).ToArray();
        BuildComponents(Section("Mods", NewsDiagnosticText.Get("mods", mods.Length), false), mods);
        BuildComponents(Section("Plugins", NewsDiagnosticText.Get("plugins", Count(DiagnosticComponentKind.Plugin)), true),
            _snapshot.Components.Where(component => component.Kind == DiagnosticComponentKind.Plugin));
        BuildComponents(Section("Dependencies", NewsDiagnosticText.Get("dependencies", Count(DiagnosticComponentKind.Dependency)), true),
            _snapshot.Components.Where(component => component.Kind == DiagnosticComponentKind.Dependency));
        var runtime = Section("Runtime", NewsDiagnosticText.Get("runtime"), true);
        foreach (var value in _snapshot.Runtime)
            Text(runtime, "News.Runtime." + value.Key, NewsDiagnosticText.Get("runtime." + value.Key) + ": " + Known(value.Value), 18, AnomalyUiStyle.Secondary);
        var history = Section("History", NewsDiagnosticText.Get("history", _snapshot.History.Length), true);
        if (_snapshot.History.Length == 0) Text(history, "News.History.Empty", NewsDiagnosticText.Get("empty"), 18, AnomalyUiStyle.Secondary);
        foreach (var issue in _snapshot.History) BuildIssue(history, issue);
        PlaceSocial();
        Canvas.ForceUpdateCanvases();
        _scroll.verticalNormalizedPosition = scrollPosition;
        if (selected != null && events != null)
        {
            var replacement = GetComponentsInChildren<Selectable>().FirstOrDefault(control => control.gameObject.name == selected && control.IsActive());
            if (replacement != null) events.SetSelectedGameObject(replacement.gameObject);
        }
        _revision = _snapshot.Revision;
        _hasRendered = _committed;
        if (_committed && hasData) _hasDataShown = true;
        _dirty = false;
    }

    [HideFromIl2Cpp]
    private int Count(DiagnosticComponentKind kind) => _snapshot.Components.Count(component => component.Kind == kind);

    [HideFromIl2Cpp]
    private Transform Section(string key, string title, bool foldout)
    {
        var group = Make("News.Section." + key, _content);
        Flow(group, 7, 0);
        if (foldout)
            Button(group.transform, "News.Section.Header." + key, (_expanded.Contains(key) ? "− " : "+ ") + title, () => Toggle(key));
        else
            Text(group.transform, "News.Section.Header." + key, title, 23, AnomalyUiStyle.Accent);
        var body = Make("News.Section.Body." + key, group.transform);
        Flow(body, 8, 0);
        body.SetActive(!foldout || _expanded.Contains(key));
        return body.transform;
    }

    [HideFromIl2Cpp]
    private void BuildComponents(Transform parent, IEnumerable<DiagnosticComponent> components)
    {
        bool any = false;
        foreach (var component in components)
        {
            any = true;
            var row = Card(parent, "News.Component." + component.Key);
            var heading = Make("News.Component.Heading." + component.Key, row.transform);
            if (_stacked) Flow(heading, 4, 0);
            else
            {
                var horizontal = heading.AddComponent<HorizontalLayoutGroup>();
                horizontal.spacing = 12;
                horizontal.childControlWidth = true;
                horizontal.childControlHeight = true;
                horizontal.childForceExpandWidth = false;
                horizontal.childForceExpandHeight = false;
            }
            var name = Text(heading.transform, "News.Component.Name." + component.Key, Known(component.Name), 22, AnomalyUiStyle.Text);
            name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            bool reported = _snapshot.CurrentIssues.Any(issue => issue.ComponentKey == component.Key && issue.Severity != DiagnosticSeverity.Info);
            string metadata = Known(component.Version) + " · " + NewsDiagnosticText.Get("kind." + component.Kind.ToString().ToLowerInvariant())
                + " · " + NewsDiagnosticText.Get("state." + component.State.ToString().ToLowerInvariant());
            if (reported) metadata += " · " + NewsDiagnosticText.Get("issues_reported");
            var meta = Text(heading.transform, "News.Component.Metadata." + component.Key, metadata, 18,
                component.State == DiagnosticLoadState.Failed || reported ? AnomalyUiStyle.Error : AnomalyUiStyle.Secondary);
            if (!_stacked) meta.gameObject.AddComponent<LayoutElement>().preferredWidth = 300;
            if (!string.IsNullOrEmpty(component.Id) || !string.IsNullOrEmpty(component.Path) || !string.IsNullOrEmpty(component.TypeName))
            {
                string key = "Component." + component.Key;
                Button(row.transform, "News.Component.Details." + component.Key,
                    (_expanded.Contains(key) ? "− " : "+ ") + NewsDiagnosticText.Get("details"), () => Toggle(key));
                if (_expanded.Contains(key))
                {
                    if (!string.IsNullOrEmpty(component.Id)) Text(row.transform, "News.Component.Id." + component.Key, NewsDiagnosticText.Get("id", component.Id), 17, AnomalyUiStyle.Secondary);
                    if (!string.IsNullOrEmpty(component.Path)) Text(row.transform, "News.Component.Path." + component.Key, NewsDiagnosticText.Get("path", DisplayPath(component.Path)), 17, AnomalyUiStyle.Secondary);
                    if (!string.IsNullOrEmpty(component.TypeName)) Text(row.transform, "News.Component.Type." + component.Key, NewsDiagnosticText.Get("type", component.TypeName), 17, AnomalyUiStyle.Secondary);
                }
            }
        }
        if (!any) Text(parent, parent.name + ".Empty", NewsDiagnosticText.Get("empty"), 18, AnomalyUiStyle.Secondary);
    }

    [HideFromIl2Cpp]
    private void BuildIssue(Transform parent, DiagnosticIssue issue)
    {
        var row = Card(parent, "News.Issue." + issue.Key);
        if (!string.IsNullOrEmpty(issue.ComponentName))
            Text(row.transform, "News.Issue.Component." + issue.Key, issue.ComponentName, 22, AnomalyUiStyle.Text);
        Text(row.transform, "News.Issue.Kind." + issue.Key, NewsDiagnosticText.Get("kind." + issue.Kind.ToString().ToLowerInvariant()), 17, AnomalyUiStyle.Secondary);
        string evidence = NewsDiagnosticService.IssueSummary(issue) ?? string.Empty;
        string firstLine = evidence.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? NewsDiagnosticText.Get("unknown");
        bool hasLongEvidence = firstLine.Length > 240 || evidence.Trim().IndexOfAny(new[] { '\r', '\n' }) >= 0;
        string reason = firstLine.Length > 240 ? firstLine.Substring(0, 239).TrimEnd() + "…" : firstLine;
        Text(row.transform, "News.Issue.Summary." + issue.Key, reason, 19,
            issue.Severity == DiagnosticSeverity.Info ? AnomalyUiStyle.Secondary : AnomalyUiStyle.Error);
        Transform details = row.transform;
        if (hasLongEvidence)
        {
            string key = "Issue." + issue.Key;
            Button(row.transform, "News.Issue.Details." + issue.Key,
                (_expanded.Contains(key) ? "− " : "+ ") + NewsDiagnosticText.Get("details"), () => Toggle(key));
            var body = Make("News.Issue.Evidence." + issue.Key, row.transform);
            Flow(body, 5, 0);
            body.SetActive(_expanded.Contains(key));
            details = body.transform;
            Text(details, "News.Issue.FullSummary." + issue.Key, evidence, 18, AnomalyUiStyle.Secondary);
        }
        Text(details, "News.Issue.Source." + issue.Key, NewsDiagnosticText.Get("source", issue.Source), 16, AnomalyUiStyle.Secondary);
        if (!string.IsNullOrEmpty(issue.Code)) Text(details, "News.Issue.Code." + issue.Key, NewsDiagnosticText.Get("code", issue.Code), 16, AnomalyUiStyle.Secondary);
        if (issue.Timestamp.HasValue)
            Text(details, "News.Issue.Time." + issue.Key, NewsDiagnosticText.Get("time", issue.Timestamp.Value.ToLocalTime().ToString("g")), 16, AnomalyUiStyle.Secondary);
    }

    [HideFromIl2Cpp]
    private void PlaceSocial()
    {
        if (_nativeControls.Count == 0) return;
        var rect = _social.GetComponent<RectTransform>();
        if (_wide)
        {
            rect.anchorMin = new Vector2(1, 0);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(Mathf.Clamp(_width * 0.25f, 280, 380), 0);
        }
        else
        {
            _social.transform.SetParent(_content, false);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
        }
        // Use the panel's newly sized rect rather than the previous layout pass's
        // Content rect. Narrow content loses 28 panel padding, 14 scrollbar space,
        // four content padding, and sixteen social padding canvas units.
        float width = _wide ? rect.sizeDelta.x - 16 : _panel.rect.width - 28 - 14 - 4 - 16;
        int columns = Math.Min(_nativeControls.Count, Math.Max(1, (int)((width + 12) / 124)));
        _socialGrid.constraintCount = columns;
        float cellWidth = Math.Max(1, Math.Min(112, width));
        _socialGrid.cellSize = new Vector2(cellWidth, 112);
        _socialGrid.GetComponent<LayoutElement>().preferredHeight = (float)Math.Ceiling((double)_nativeControls.Count / columns) * 124 - 12;
        _social.SetActive(true);
    }

    [HideFromIl2Cpp]
    private void Toggle(string key)
    {
        if (!_expanded.Add(key)) _expanded.Remove(key);
        _dirty = true;
    }

    [HideFromIl2Cpp]
    private static string Known(string value) => string.IsNullOrWhiteSpace(value) || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
        ? NewsDiagnosticText.Get("unknown") : value;

    [HideFromIl2Cpp]
    private static string DisplayPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return NewsDiagnosticText.Get("unknown");
        string normalized = path.Replace('\\', '/');
        string gameRoot = System.IO.Path.GetDirectoryName(MelonEnvironment.MelonLoaderDirectory)?.Replace('\\', '/').TrimEnd('/') + "/";
        if (gameRoot.Length > 1 && normalized.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
            return "<game>/" + normalized.Substring(gameRoot.Length);
        if (System.IO.Path.IsPathRooted(path) || normalized.Split('/').Contains(".."))
            return System.IO.Path.GetFileName(normalized);
        return normalized;
    }

    [HideFromIl2Cpp]
    private void CopySummary()
    {
        try
        {
            GUIUtility.systemCopyBuffer = NewsDiagnosticService.CopySummary();
            _copyLabel.text = NewsDiagnosticText.Get("copied");
        }
        catch (Exception exception)
        {
            _copyLabel.text = NewsDiagnosticText.Get("copy_failed");
            MelonLogger.Warning("News diagnostics copy failed: " + exception.Message);
        }
        _copyReset = Time.realtimeSinceStartupAsDouble + 3;
    }

    [HideFromIl2Cpp]
    private void KeepSelectionVisible()
    {
        var events = EventSystem.current;
        if (events == null) return;
        var selected = events.currentSelectedGameObject;
        if (selected == null || selected == _lastSelected || !selected.transform.IsChildOf(_content)) return;
        _lastSelected = selected;
        var target = selected.GetComponent<RectTransform>();
        if (target == null) return;
        Canvas.ForceUpdateCanvases();
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        for (int corner = 0; corner < 4; corner++)
        {
            var rect = target.rect;
            var local = new Vector3((corner & 1) == 0 ? rect.xMin : rect.xMax, (corner & 2) == 0 ? rect.yMin : rect.yMax);
            float y = _scroll.viewport.InverseTransformPoint(target.TransformPoint(local)).y;
            min = Math.Min(min, y);
            max = Math.Max(max, y);
        }
        var viewport = _scroll.viewport.rect;
        float delta = min < viewport.yMin ? viewport.yMin - min : max > viewport.yMax ? viewport.yMax - max : 0;
        if (Mathf.Abs(delta) > 0.01f) _content.anchoredPosition += new Vector2(0, delta);
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (!_committed) return;
        foreach (var control in _nativeControls) control.Restore();
        if (_nativeHeader != null) _nativeHeader.SetActive(_headerWasActive);
        if (_nativeArticle != null) _nativeArticle.SetActive(_articleWasActive);
        if (_nativeThumbnail != null) _nativeThumbnail.SetActive(_thumbnailWasActive);
    }

    [HideFromIl2Cpp]
    private static GameObject Make(string name, Transform parent)
    {
        var go = AnomalyUiStyle.MakeUi(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    [HideFromIl2Cpp]
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    [HideFromIl2Cpp]
    private static VerticalLayoutGroup Flow(GameObject go, int spacing, int padding)
    {
        var flow = go.AddComponent<VerticalLayoutGroup>();
        flow.spacing = spacing;
        flow.padding = new RectOffset { left = padding, right = padding, top = padding, bottom = padding };
        flow.childControlWidth = true;
        flow.childControlHeight = true;
        flow.childForceExpandWidth = true;
        flow.childForceExpandHeight = false;
        flow.childAlignment = TextAnchor.UpperLeft;
        return flow;
    }

    [HideFromIl2Cpp]
    private static TMP_Text Text(Transform parent, string name, string value, float size, Color color)
    {
        var text = Make(name, parent).AddComponent<TextMeshProUGUI>();
        AnomalyUiStyle.ApplyText(text, size);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.text = value ?? string.Empty;
        text.color = color;
        return text;
    }

    [HideFromIl2Cpp]
    private static GameObject Card(Transform parent, string name)
    {
        var row = Make(name, parent);
        var image = row.AddComponent<Image>();
        image.color = AnomalyUiStyle.Input;
        image.raycastTarget = false;
        Flow(row, 5, 10);
        return row;
    }

    [HideFromIl2Cpp]
    private static GameObject Button(Transform parent, string name, string label, Action action)
    {
        var go = Make(name, parent);
        var image = go.AddComponent<Image>();
        image.color = AnomalyUiStyle.Input;
        var button = go.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        AnomalyUiStyle.ApplySelectable(button);
        Flow(go, 0, 6);
        go.AddComponent<LayoutElement>().minHeight = 32;
        Text(go.transform, name + ".Label", label, 19, AnomalyUiStyle.Text);
        button.onClick.AddListener((UnityEngine.Events.UnityAction)(() => action()));
        return go;
    }

    private sealed class NativeControl
    {
        internal readonly RectTransform Rect;
        private readonly Transform _parent;
        private readonly int _index;
        private readonly Vector2 _anchorMin, _anchorMax, _position, _size, _pivot;
        private readonly Vector3 _scale;
        private readonly RectTransform _clickRect;
        private readonly Vector2 _clickAnchorMin, _clickAnchorMax, _clickPosition, _clickSize, _clickPivot;
        private readonly Animator _animator;
        private readonly bool _animatorWasEnabled;

        internal NativeControl(RectTransform rect, int index, RectTransform clickRect)
        {
            Rect = rect;
            if (rect == null) return;
            _parent = rect.parent;
            _index = index;
            _anchorMin = rect.anchorMin;
            _anchorMax = rect.anchorMax;
            _position = rect.anchoredPosition;
            _size = rect.sizeDelta;
            _pivot = rect.pivot;
            _scale = rect.localScale;
            _clickRect = clickRect;
            if (clickRect == null || clickRect == rect) return;
            _clickAnchorMin = clickRect.anchorMin;
            _clickAnchorMax = clickRect.anchorMax;
            _clickPosition = clickRect.anchoredPosition;
            _clickSize = clickRect.sizeDelta;
            _clickPivot = clickRect.pivot;
            _animator = clickRect.GetComponent<Animator>();
            _animatorWasEnabled = _animator != null && _animator.enabled;
        }

        internal void MoveTo(Transform grid)
        {
            if (Rect == null) return;
            Rect.SetParent(grid, false);
            if (_clickRect == null || _clickRect == Rect) return;
            // Stock hover clips animate this Image's width from 120 to 280.
            // That fixed width would fight the responsive grid and overlap other
            // controls. Retain its click/audio components and ordinary Button tint.
            if (_animator != null) _animator.enabled = false;
            Stretch(_clickRect);
            _clickRect.pivot = new Vector2(0.5f, 0.5f);
        }

        internal void Restore()
        {
            if (Rect == null || _parent == null) return;
            Rect.SetParent(_parent, false);
            Rect.SetSiblingIndex(_index);
            Rect.anchorMin = _anchorMin;
            Rect.anchorMax = _anchorMax;
            Rect.anchoredPosition = _position;
            Rect.sizeDelta = _size;
            Rect.pivot = _pivot;
            Rect.localScale = _scale;
            if (_clickRect == null || _clickRect == Rect) return;
            _clickRect.anchorMin = _clickAnchorMin;
            _clickRect.anchorMax = _clickAnchorMax;
            _clickRect.anchoredPosition = _clickPosition;
            _clickRect.sizeDelta = _clickSize;
            _clickRect.pivot = _clickPivot;
            if (_animator != null) _animator.enabled = _animatorWasEnabled;
        }
    }
}
