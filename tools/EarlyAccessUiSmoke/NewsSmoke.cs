using System.Reflection;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Localization;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private static readonly (int, int)[] NewsSizes =
        { (1280, 720), (1920, 1080), (3440, 1440), (1024, 768), (1280, 1024) };
    private const string NewsRootName = "Anomaly.NewsDiagnostics";
    private const string NewsNamespace = "Anomaly.Client.Diagnostics.News.";
    private bool _newsSmokeOnly;
    private object _newsOriginalLocalization;
    private int _newsOldRoot;
    private int _newsSocialControls;
    private float _newsScrollBeforeResize;
    private string[] _newsExpandedBeforeResize;
    private string _newsOriginalClipboard;
    private bool _newsClipboardCaptured;

    private static Type NewsPresenterType => Type.GetType(
        "Anomaly.Client.Components.News.NewsDiagnosticsPresenter, Anomaly.Client", true);
    private static object NewsPresenter => NewsPresenterType.GetProperty("Active", All).GetValue(null);
    private static GameObject NewsRoot => GameObject.Find(NewsRootName);
    private static ScrollRect NewsScroll => NewsRoot.GetComponentInChildren<ScrollRect>();

    private void EnqueueNewsSmoke()
    {
        Add("news-profile-ready", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            {
                Input().SetTextWithoutNotify("News UI Smoke");
                Submit();
                Require(!IsVisible(), "News fixture saves a private first-run nickname");
            }
        });
        foreach (var size in NewsSizes)
        {
            int width = size.Item1, height = size.Item2;
            Add($"news-resolution-{width}x{height}", () => Screen.SetResolution(width, height, false));
            EnqueueNewsResolution(width, height);
        }
        Add("news-russian-menu-reload", () =>
        {
            SetNewsRussianLocale();
            ReloadNewsMenu();
        });
        AddWait("news-russian-menu-recreated", () => NewsRoot != null && NewsRoot.GetInstanceID() != _newsOldRoot, 45);
        Add("news-fixture-russian-many", () =>
        {
            SetNewsFixture(BuildNewsFixture());
            ExpandNewsSections();
        });
        foreach (var size in NewsSizes)
        {
            int width = size.Item1, height = size.Item2;
            Add($"news-many-resize-{width}x{height}", () => Screen.SetResolution(width, height, false));
            AddWait($"news-many-actual-{width}x{height}", () => Screen.width == width && Screen.height == height, 30);
            Add("news-many-top", () => { ShowNews(); NewsScroll.verticalNormalizedPosition = 1; });
            Add("news-many-layout", () =>
            {
                AssertNewsTopReachable();
                AssertNewsGeometry("many");
                Require(Tr.ActiveLocale == "ru", "News fixture uses the actual Russian translation service");
                var header = NewsRect("News.Header").GetComponentInChildren<TMP_Text>();
                Require(header.text.Any(character => character >= '\u0400' && character <= '\u04ff'),
                    "News header renders Russian translations");
                Require(NewsScroll.content.rect.height > NewsScroll.viewport.rect.height + 100,
                    "Many-entry fixture requires real vertical scrolling");
                Capture("news-russian-many-top");
            });
            Add("news-many-scroll-bottom", () => NewsScroll.verticalNormalizedPosition = 0);
            Add("news-many-bottom", () =>
            {
                AssertNewsBottomReachable();
                Capture("news-russian-many-bottom");
            });
        }
        Add("news-summary-copy", () =>
        {
            _newsOriginalClipboard = GUIUtility.systemCopyBuffer;
            _newsClipboardCaptured = true;
            var button = NewsRoot.GetComponentsInChildren<Button>().Single(item => item.name == "News.CopySummary");
            button.OnSubmit(new BaseEventData(EventSystem.current));
            string summary = GUIUtility.systemCopyBuffer;
            Require(summary.Contains(Tr.Get("anomaly.news.diagnostics.runtime"), StringComparison.Ordinal) &&
                summary.Contains(Tr.Get("anomaly.news.diagnostics.runtime.graphics"), StringComparison.Ordinal) &&
                summary.Contains("D3D12", StringComparison.Ordinal), "Copy Summary writes translated runtime labels and values to the engine clipboard");
            Require(summary.Contains("Example.Dependency", StringComparison.Ordinal) && summary.Contains("9.8.7.6", StringComparison.Ordinal),
                "Copy Summary retains structured dependency identity and version");
            Require(!summary.Contains("SMOKE_PRIVATE_TOKEN", StringComparison.Ordinal) &&
                !summary.Contains("203.0.113.17", StringComparison.Ordinal) &&
                !summary.Contains(@"C:\Users\Example", StringComparison.Ordinal) &&
                !summary.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase),
                "Copy Summary omits fixture secrets, endpoints and absolute user paths");
            GUIUtility.systemCopyBuffer = _newsOriginalClipboard;
            _newsClipboardCaptured = false;
        });
        Add("news-compact-issue", () =>
        {
            var summary = NewsRoot.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "News.Issue.Summary.smoke-unattributed");
            Require(summary.fontSize == 19 && summary.text.Length <= 240 && !summary.text.Contains('\n'),
                "Diagnostic overview keeps a fixed readable font and a compact first-line summary");
            var details = NewsRoot.GetComponentsInChildren<Button>().Single(item => item.name == "News.Issue.Details.smoke-unattributed");
            details.OnSubmit(new BaseEventData(EventSystem.current));
        });
        Add("news-full-evidence", () =>
        {
            var evidence = NewsRoot.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "News.Issue.FullSummary.smoke-unattributed");
            Require(evidence.text.Contains("First fixture stack frame", StringComparison.Ordinal) && evidence.text.Contains('\n'),
                "Diagnostic Details reveals the multiline evidence behind the compact overview");
            Require(!evidence.text.Contains("SMOKE_PRIVATE_TOKEN", StringComparison.Ordinal), "Diagnostic Details keeps fixture credentials redacted");
            Capture("news-diagnostic-details");
        });
        Add("news-foldout-and-scroll-save", () =>
        {
            SetNewsSection("Dependencies", false);
            NewsScroll.verticalNormalizedPosition = 0.37f;
        });
        Add("news-tab-away-state", () =>
        {
            _newsScrollBeforeResize = NewsScroll.verticalNormalizedPosition;
            _newsExpandedBeforeResize = NewsExpanded();
            ApiClient.MainMenu.ChangeMenu(1);
        });
        Add("news-tab-return", ShowNews);
        Add("news-tab-state", () =>
        {
            Require(_newsExpandedBeforeResize.SequenceEqual(NewsExpanded()), "News tab return preserves expanded sections");
            Require(Math.Abs(NewsScroll.verticalNormalizedPosition - _newsScrollBeforeResize) < 0.04f,
                "News tab return preserves scroll position");
        });
        Add("news-live-resize", () => Screen.SetResolution(1024, 768, false));
        AddWait("news-live-resize-actual", () => Screen.width == 1024 && Screen.height == 768, 30);
        Add("news-live-resize-state", () =>
        {
            Require(_newsExpandedBeforeResize.SequenceEqual(NewsExpanded()), "Actual window resize preserves expanded sections");
            Require(Math.Abs(NewsScroll.verticalNormalizedPosition - _newsScrollBeforeResize) < 0.06f,
                "Actual window resize preserves normalized scroll position");
            AssertNewsGeometry("resized");
            Capture("news-state-after-resize");
        });
        Add("news-stock-async-write", () =>
        {
            var loader = ActiveNewsLoader();
            // TextProcessor consumes the Steam NewsRaw JSON envelope, not article text.
            // One ordinary announcement also exercises the native article update path.
            string response = System.Text.Json.JsonSerializer.Serialize(new
            {
                appnews = new
                {
                    appid = 700330,
                    newsitems = new[] { new
                    {
                        gid = "1", title = "STOCK ASYNC ARTICLE OVERWRITE FIXTURE", url = "", is_external_url = false,
                        author = "UI smoke", contents = "STOCK ASYNC ARTICLE OVERWRITE FIXTURE",
                        feedlabel = "Steam Community Announcements", date = 1700000000L,
                        feedname = "steam_community_announcements", feedtype = 1,
                    } },
                    count = 1,
                },
            });
            loader.TextProcessor(response);
            int fixtureIndex = -1;
            if (loader._announcements != null)
                for (int index = 0; index < loader._announcements.Count; index++)
                    if (loader._announcements[index].Title == "STOCK ASYNC ARTICLE OVERWRITE FIXTURE") fixtureIndex = index;
            Require(fixtureIndex >= 0, "Stock TextProcessor consumes a valid NewsRaw response and creates the fixture announcement");
            loader.ShowAnnouncement(fixtureIndex);
        });
        Add("news-stock-async-survival", () =>
        {
            Require(NewsRoot != null && !ActiveNewsLoader().ArticleText.gameObject.activeInHierarchy,
                "Stock article processing keeps the owned diagnostics panel visible");
            Require(NewsRoot.GetComponentsInChildren<TMP_Text>().Any(text => text.text.Contains("Smoke Mod", StringComparison.Ordinal)),
                "Stock article processing does not replace the fixture inventory");
            Capture("news-after-stock-article-write");
        });
        Add("news-installer-idempotent", () =>
        {
            InstallNewsPanel();
            InstallNewsPanel();
            Require(NewsRootCount() == 1, "Repeated News installer calls preserve one owned root");
        });
        Add("news-menu-reload", () =>
        {
            ClearNewsFixture();
            RestoreNewsLocale();
            ReloadNewsMenu();
        });
        AddWait("news-menu-recreated", () => NewsRoot != null && NewsRoot.GetInstanceID() != _newsOldRoot, 45);
        Add("news-menu-recreated-check", () =>
        {
            ShowNews();
            Require(NewsRootCount() == 1, "Actual menu scene recreation restores exactly one owned News root");
            Require(NewsSocialControlCount() == _newsSocialControls, "Actual menu scene recreation preserves every native social button");
            AssertNewsGeometry("recreated");
            Capture("news-recreated");
        });
        Add("news-complete", () =>
        {
            CleanupNewsFixtures();
            File.WriteAllText(Path.Combine(_output, "result.txt"), $"News UI smoke passed; {_captures} screenshots. " +
                "Real Unity layout, resolution changes, article processing, and menu scene reloads; fixture data and synthetic controls. " +
                "Gameplay transitions and physical input are not validated by NewsOnly.\n");
            Application.Quit();
        });
    }

    private void EnqueueNewsResolution(int width, int height)
    {
        AddWait($"news-actual-{width}x{height}", () => Screen.width == width && Screen.height == height, 30);
        Add("news-open", ShowNews);
        AddWait("news-owned-panel", () => NewsRoot != null && NewsPresenter != null, 20);
        if (width == 1280 && height == 720)
        {
            AddWait("news-live-inventory-collected", () =>
            {
                var service = Type.GetType(NewsNamespace + "NewsDiagnosticService, Anomaly.Client", true);
                var snapshot = service.GetProperty("Snapshot", All).GetValue(null);
                return snapshot != null && !(bool)snapshot.GetType().GetProperty("IsCollecting", All).GetValue(snapshot);
            }, 30);
            Add("news-default-initial-position", () =>
            {
                WriteNewsHierarchyEvidence("default-initial");
                AssertNewsTopReachable();
                Require(NewsScroll.verticalNormalizedPosition >= 0.98f,
                    "The first collected News overview defaults to the top before any fixture scroll assignment");
            });
        }
        Add("news-live-top", () =>
        {
            Canvas.ForceUpdateCanvases();
            NewsScroll.StopMovement();
            NewsScroll.verticalNormalizedPosition = 1;
        });
        Add("news-layout", () =>
        {
            WriteNewsHierarchyEvidence("live");
            AssertNewsTopReachable();
            AssertNewsGeometry("live");
            Capture("news");
        });
    }

    private void AssertNewsReturnedMenu(int cycle)
    {
        ShowNews();
        Require(NewsRoot != null && NewsRootCount() == 1, "Returning from gameplay restores one owned News panel");
        Capture("news-returned-menu-" + cycle);
    }

    private void ShowNews()
    {
        Call("HideAll");
        var menu = ApiClient.MainMenu;
        int index = Array.FindIndex(menu.submenus.ToArray(), item => item.name.Contains("News", StringComparison.OrdinalIgnoreCase));
        Require(index >= 0, "Main menu contains a News submenu");
        menu.ChangeMenu(index);
    }

    private static NewsLoader ActiveNewsLoader() => Resources.FindObjectsOfTypeAll<NewsLoader>()
        .First(loader => loader != null && loader.gameObject.activeInHierarchy);

    private static int NewsRootCount() => Resources.FindObjectsOfTypeAll<RectTransform>()
        .Count(rect => rect != null && rect.name == NewsRootName);

    private static int NewsSocialControlCount()
    {
        var grid = NewsRect("News.Social.Grid");
        int count = 0;
        for (int index = 0; index < grid.childCount; index++)
            if (grid.GetChild(index).gameObject.activeSelf) count++;
        return count;
    }

    private void ReloadNewsMenu()
    {
        _newsOldRoot = NewsRoot.GetInstanceID();
        _newsSocialControls = NewsSocialControlCount();
        Require(_newsSocialControls > 0, "Menu recreation starts with real native social tiles to preserve");
        string scene = ActiveNewsLoader().gameObject.scene.name;
        Require(!string.IsNullOrEmpty(scene) && scene.Contains("MainMenu", StringComparison.OrdinalIgnoreCase),
            "Menu recreation reloads the current menu scene only");
        SceneManager.LoadScene(scene);
    }

    private void AssertNewsGeometry(string fixture)
    {
        Canvas.ForceUpdateCanvases();
        var root = NewsRoot.GetComponent<RectTransform>();
        var scroll = NewsScroll;
        Rect screen = new(0, 0, Screen.width, Screen.height);
        var evidence = new List<object>();
        foreach (var name in new[] { "News.Header", "News.ScrollView", "News.Footer" })
        {
            var rect = NewsRect(name);
            Rect bounds = NewsScreenBounds(rect);
            Require(Contains(screen, bounds, 2), name + " remains inside the actual screen");
            evidence.Add(new { name, x = bounds.x, y = bounds.y, width = bounds.width, height = bounds.height });
        }
        Rect header = NewsScreenBounds(NewsRect("News.Header"));
        Rect viewport = NewsScreenBounds(scroll.viewport);
        Rect footer = NewsScreenBounds(NewsRect("News.Footer"));
        Require(!Overlaps(header, viewport) && !Overlaps(footer, viewport), "News header and footer do not overlap the scrolling viewport");
        Require(scroll.vertical && !scroll.horizontal, "News inventory uses vertical scrolling without a horizontal scrollbar");
        var content = scroll.content;
        Require(content.rect.width <= scroll.viewport.rect.width + 2, "News content fits the viewport width");
        AssertSiblingRows(content);
        foreach (var text in NewsRoot.GetComponentsInChildren<TMP_Text>())
        {
            if (!text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text)) continue;
            if (text.name.StartsWith("News.", StringComparison.Ordinal))
                Require(!text.richText, "News metadata and diagnostic text disable rich markup: " + text.name);
            Require(text.rectTransform.rect.height + 3 >= text.preferredHeight,
                "News text has enough height: " + text.name);
        }
        var social = GameObject.Find("News.Social.Grid");
        int nativeSocialCount = ActiveNewsLoader().GetComponentsInChildren<SocialMediaButton>(false).Length;
        Require(nativeSocialCount == 10, "Supported stock NewsLoader contains all ten active native SocialMediaButton controls");
        Require(social != null && social.activeInHierarchy, "News social grid is present and active");
        Require(NewsSocialControlCount() > 0 && NewsSocialControlCount() == nativeSocialCount,
            "Every active native social tile is hosted by the owned social grid");
        Require(social.GetComponentsInChildren<SocialMediaButton>(false).Length == nativeSocialCount,
            "Native social handlers remain beneath their hosted tiles");
        if (social != null && social.activeInHierarchy)
        {
            var rect = social.GetComponent<RectTransform>();
            Rect bounds = NewsScreenBounds(rect);
            if (!rect.IsChildOf(content))
            {
                Require(Contains(screen, bounds, 2), "News social grid remains inside the actual screen");
                Require(!Overlaps(viewport, bounds), "News diagnostics and social sidebar do not overlap");
            }
            AssertSiblingRows(rect, checkVertical: false);
            foreach (var marker in social.GetComponentsInChildren<SocialMediaButton>(false))
            {
                var imageRect = marker.GetComponent<RectTransform>();
                Require(imageRect != null, "Native social image exposes its actual RectTransform");
                var tile = imageRect;
                while (tile.parent != null && tile.parent != rect) tile = tile.parent.TryCast<RectTransform>();
                Require(tile != null && tile.parent == rect, "Native social image belongs to a direct responsive grid tile");
                Require(Contains(tile.rect, NewsRelativeBounds(tile, imageRect), 2),
                    "Nested native social image fits its grid tile despite original hover animation: " + marker.name);
            }
        }
        File.WriteAllText(Path.Combine(_output, $"{Screen.width}x{Screen.height}-news-{fixture}-layout.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                width = Screen.width, height = Screen.height, roots = NewsRootCount(),
                nativeSocialHandlers = nativeSocialCount, ownedSocialTiles = NewsSocialControlCount(),
                expanded = NewsExpanded(), normalizedScroll = scroll.verticalNormalizedPosition,
                viewportWidth = scroll.viewport.rect.width, viewportHeight = scroll.viewport.rect.height,
                contentWidth = content.rect.width, contentHeight = content.rect.height, fixedRegions = evidence
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private void AssertSiblingRows(RectTransform parent, bool checkVertical = true)
    {
        Rect? previous = null;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i).TryCast<RectTransform>();
            if (child == null || !child.gameObject.activeSelf) continue;
            Rect bounds = NewsRelativeBounds(parent, child);
            Require(bounds.xMin >= parent.rect.xMin - 2 && bounds.xMax <= parent.rect.xMax + 2,
                "News row remains inside its content width: " + child.name);
            if (checkVertical && previous.HasValue)
                Require(!Overlaps(previous.Value, bounds), "News adjacent rows do not overlap: " + child.name);
            previous = bounds;
        }
    }

    private void AssertNewsBottomReachable()
    {
        Canvas.ForceUpdateCanvases();
        var scroll = NewsScroll;
        Rect content = NewsRelativeBounds(scroll.viewport, scroll.content);
        Require(Math.Abs(content.yMin - scroll.viewport.rect.yMin) < 4,
            "News vertical scroll reaches the end of its content");
        Require(Math.Abs(scroll.verticalNormalizedPosition) < 0.02f, "News scroll reports its bottom position");
        var grid = NewsRect("News.Social.Grid");
        if (grid.IsChildOf(scroll.content))
            foreach (var marker in grid.GetComponentsInChildren<SocialMediaButton>(false))
                Require(Contains(NewsScreenBounds(scroll.viewport), NewsScreenBounds(marker.GetComponent<RectTransform>()), 2),
                    "Scrolling to the bottom visibly reaches the native social tile: " + marker.name);
    }

    private void AssertNewsTopReachable()
    {
        Canvas.ForceUpdateCanvases();
        var scroll = NewsScroll;
        Rect content = NewsRelativeBounds(scroll.viewport, scroll.content);
        Require(Math.Abs(content.yMax - scroll.viewport.rect.yMax) < 4,
            "News overview starts at the actual top of its content");
        if (scroll.content.rect.height > scroll.viewport.rect.height + 1)
            Require(Math.Abs(scroll.verticalNormalizedPosition - 1) < 0.02f,
                "Initial News capture is at the top rather than silently showing the bottom");
        var heading = NewsRect("News.Section.Header.Issues");
        Require(heading.gameObject.activeInHierarchy && Contains(NewsScreenBounds(scroll.viewport), NewsScreenBounds(heading), 2),
            "Current-launch issues heading is visibly reachable in the initial News overview");
    }

    private void WriteNewsHierarchyEvidence(string fixture)
    {
        var loader = ActiveNewsLoader();
        var nodes = loader.GetComponentsInChildren<Transform>(true).ToArray();
        var hierarchy = nodes.Take(3000).Select(node =>
        {
            var names = new List<string>();
            for (var cursor = node; cursor != null && cursor != loader.transform.parent; cursor = cursor.parent)
                names.Insert(0, cursor.name);
            var rect = node.TryCast<RectTransform>();
            Rect bounds = rect == null ? default : NewsScreenBounds(rect);
            var components = node.GetComponents<Component>().Select(component =>
                component == null ? "missing" : component.GetIl2CppType().FullName).ToArray();
            return new
            {
                path = string.Join("/", names), node.gameObject.activeSelf, node.gameObject.activeInHierarchy,
                components, hasRect = rect != null,
                rect = new { x = bounds.x, y = bounds.y, width = bounds.width, height = bounds.height },
                socialMarker = node.GetComponent<SocialMediaButton>() != null,
                button = node.GetComponent<Button>() != null,
            };
        }).ToArray();
        File.WriteAllText(Path.Combine(_output, $"{Screen.width}x{Screen.height}-news-{fixture}-hierarchy.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                loader = loader.name, nodes = nodes.Length, truncated = nodes.Length > 3000,
                nativeSocialMarkers = loader.GetComponentsInChildren<SocialMediaButton>(true).Length,
                nativeActiveSocialMarkers = loader.GetComponentsInChildren<SocialMediaButton>(false).Length,
                gridChildren = NewsSocialControlCount(), hierarchy,
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static Rect NewsRelativeBounds(Transform reference, RectTransform rect) => NewsBounds(rect,
        point => reference.InverseTransformPoint(point));

    private static Rect NewsScreenBounds(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return NewsBounds(rect, point => RectTransformUtility.WorldToScreenPoint(camera, point));
    }

    // Generated IL2CPP wrappers do not implement CalculateRelativeRectTransformBounds.
    private static Rect NewsBounds(RectTransform rect, Func<Vector3, Vector3> transform)
    {
        Rect local = rect.rect;
        var corners = new[] { new Vector3(local.xMin, local.yMin), new Vector3(local.xMin, local.yMax),
            new Vector3(local.xMax, local.yMax), new Vector3(local.xMax, local.yMin) };
        var points = corners.Select(point => transform(rect.TransformPoint(point))).ToArray();
        return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y),
            points.Max(point => point.x), points.Max(point => point.y));
    }

    private static bool Contains(Rect outer, Rect inner, float tolerance) => inner.xMin >= outer.xMin - tolerance &&
        inner.yMin >= outer.yMin - tolerance && inner.xMax <= outer.xMax + tolerance && inner.yMax <= outer.yMax + tolerance;
    private static bool Overlaps(Rect left, Rect right) => Math.Min(left.xMax, right.xMax) - Math.Max(left.xMin, right.xMin) > 1 &&
        Math.Min(left.yMax, right.yMax) - Math.Max(left.yMin, right.yMin) > 1;

    private static string[] NewsExpanded() => ((IEnumerable<string>)NewsPresenterType.GetField("_expanded", All)
        .GetValue(NewsPresenter)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    private static RectTransform NewsRect(string name) => NewsRoot.GetComponentsInChildren<RectTransform>(true)
        .Single(rect => rect.name == name);

    private static void SetNewsFixture(object snapshot)
    {
        Type.GetType(NewsNamespace + "NewsDiagnosticService, Anomaly.Client", true).GetMethod("SetFixture", All)
            .Invoke(null, new[] { snapshot });
        NewsPresenterType.GetMethod("SetSmokeSnapshot", All).Invoke(NewsPresenter, new[] { snapshot });
    }
    private static void ClearNewsFixture()
    {
        if (NewsPresenter != null) NewsPresenterType.GetMethod("ClearSmokeSnapshot", All).Invoke(NewsPresenter, null);
        Type.GetType(NewsNamespace + "NewsDiagnosticService, Anomaly.Client", true).GetMethod("ClearFixture", All).Invoke(null, null);
    }
    private static void InstallNewsPanel() => Type.GetType("Anomaly.Client.Internal.UI.News.NewsDiagnosticsPanel, Anomaly.Client", true)
        .GetMethod("Install", All).Invoke(null, new object[] { ActiveNewsLoader() });

    private static void ExpandNewsSections()
    {
        foreach (string section in new[] { "Plugins", "Dependencies", "Runtime", "History" }) SetNewsSection(section, true);
    }
    private static void SetNewsSection(string section, bool expanded)
    {
        bool current = NewsExpanded().Contains(section, StringComparer.Ordinal);
        if (current == expanded) return;
        var button = NewsRoot.GetComponentsInChildren<Button>().Single(item => item.name == "News.Section.Header." + section);
        button.OnSubmit(new BaseEventData(EventSystem.current));
    }

    private object BuildNewsFixture()
    {
        var components = new List<object>();
        foreach (var item in new[] { ("Mod", 60), ("Plugin", 8), ("Dependency", 40), ("ManagedModule", 5) })
            for (int i = 0; i < item.Item2; i++)
                components.Add(NewsModel("DiagnosticComponent", ("Key", "smoke-" + item.Item1 + "-" + i),
                    ("Name", i == 0 ? "Smoke Mod <b>literal markup</b> — очень длинное название диагностического компонента для проверки переноса строк" : "Smoke " + item.Item1 + " " + i),
                    ("Version", "1.2.3-preview.with.a.deliberately.long.version"), ("Id", "smoke.example." + i),
                    ("Path", "Mods/Smoke" + i + ".dll"), ("Kind", item.Item1), ("State", i == 0 ? "Failed" : "Loaded")));
        var issues = new[]
        {
            NewsModel("DiagnosticIssue", ("Key", "smoke-missing"), ("ComponentKey", "smoke-Mod-0"),
                ("ComponentName", "Smoke Mod"), ("Kind", "MissingDependency"), ("Severity", "Error"),
                ("Code", "SMOKE_MISSING_DEPENDENCY"), ("Source", "Fixture loader evidence"),
                ("DependencyName", "Example.Dependency"), ("DependencyVersion", "9.8.7.6"),
                ("Summary", "Не удалось загрузить зависимость Example.Dependency, Version=9.8.7.6. Very long diagnostic detail wraps without hiding the component version or the error code. <size=99>literal markup</size>")),
            NewsModel("DiagnosticIssue", ("Key", "smoke-unattributed"), ("Kind", "ReportedError"), ("Severity", "Warning"),
                ("Code", "SMOKE_UNATTRIBUTED"), ("Source", "Fixture runtime error"),
                ("Summary", "An unattributed error stays in diagnostics without assigning a mod as the cause. " + new string('x', 300) +
                    "\nFirst fixture stack frame\nSecond fixture stack frame\nendpoint=203.0.113.17:27988\ntoken=SMOKE_PRIVATE_TOKEN\nfile=C:\\Users\\Example\\Secret.bin"))
        };
        var history = new[] { NewsModel("DiagnosticIssue", ("Key", "smoke-history"), ("Kind", "DiagnosticReport"),
            ("Severity", "Warning"), ("IsHistorical", true), ("Timestamp", DateTimeOffset.UtcNow.AddDays(-1)),
            ("Source", "Earlier launch fixture"), ("Code", "FirstChanceFlood"),
            ("Summary", "Earlier diagnostic activity is historical evidence and does not claim that the present component caused a crash.")) };
        var runtime = new[] { NewsModel("DiagnosticValue", ("Key", "unity"), ("Value", Application.unityVersion)),
            NewsModel("DiagnosticValue", ("Key", "graphics"), ("Value", "D3D12 — очень длинное диагностическое значение графического устройства")),
            NewsModel("DiagnosticValue", ("Key", "dotnet"), ("Value", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription)) };
        return NewsModel("NewsDiagnosticSnapshot", ("Revision", 900001), ("GameVersion", "14.2.7"),
            ("AnomalyVersion", "1.0.0"), ("LoaderVersion", "0.7.3"),
            ("Components", NewsArray("DiagnosticComponent", components)), ("CurrentIssues", NewsArray("DiagnosticIssue", issues)),
            ("History", NewsArray("DiagnosticIssue", history)), ("Runtime", NewsArray("DiagnosticValue", runtime)));
    }

    private static object NewsModel(string typeName, params (string Name, object Value)[] properties)
    {
        var type = Type.GetType(NewsNamespace + typeName + ", Anomaly.Client", true);
        var model = Activator.CreateInstance(type, nonPublic: true);
        foreach (var entry in properties)
        {
            var property = type.GetProperty(entry.Name, All);
            object value = property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, (string)entry.Value) : entry.Value;
            property.SetValue(model, value);
        }
        return model;
    }
    private static Array NewsArray(string typeName, IEnumerable<object> items)
    {
        var values = items.ToArray();
        var array = Array.CreateInstance(Type.GetType(NewsNamespace + typeName + ", Anomaly.Client", true), values.Length);
        for (int i = 0; i < values.Length; i++) array.SetValue(values[i], i);
        return array;
    }

    private void SetNewsRussianLocale()
    {
        var facade = typeof(Tr);
        var field = facade.GetField("_service", All);
        _newsOriginalLocalization ??= field.GetValue(null);
        var serviceType = Type.GetType("Anomaly.Client.Api.Localization.LocalizationService, Anomaly.Client.Api", true);
        var constructor = serviceType.GetConstructor(All, null, new[] { typeof(string), typeof(Assembly), typeof(string) }, null);
        field.SetValue(null, constructor.Invoke(new object[] { Paths.AnomalyPath, NewsPresenterType.Assembly, "ru" }));
    }
    private void RestoreNewsLocale()
    {
        if (_newsOriginalLocalization == null) return;
        typeof(Tr).GetField("_service", All).SetValue(null, _newsOriginalLocalization);
        _newsOriginalLocalization = null;
    }
    private void CleanupNewsFixtures()
    {
        if (!_newsSmokeOnly) return;
        if (_newsClipboardCaptured)
        {
            GUIUtility.systemCopyBuffer = _newsOriginalClipboard;
            _newsClipboardCaptured = false;
        }
        RestoreNewsLocale();
        try { ClearNewsFixture(); }
        catch (TypeLoadException) { }
    }
}
