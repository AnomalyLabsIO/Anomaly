using System.Diagnostics;
using System.Collections;
using System.Reflection;
using Anomaly.EarlyAccessUiSmoke;
using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using Il2CppUserSettings.GUIElements;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Anomaly.LaunchProfiler;

// A bounded menu smoke check. It drives the existing UI handlers and never changes
// game settings or joins a server. All profile writes belong to the isolated fixture.
internal static class GameProbe
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static NewMainMenu? _menu;
    private static Button? _settingsButton;
    private static UserSettingsCategories? _categories;
    private static LoadingScreen[] _loadingScreens = Array.Empty<LoadingScreen>();
    private static PhotosensitivityWarning[] _warnings = Array.Empty<PhotosensitivityWarning>();
    private static readonly Il2CppSystem.Collections.Generic.List<RaycastResult> RaycastResults = new();
    private static int _readyFrames;
    private static long _lastUpdateQpc;
    private static long _updateIntervalTicks;
    private static long _readyMaxIntervalTicks;
    private const double ResponsiveFrameThresholdMilliseconds = 100;
    private static int _stage;
    private static int _frame;
    private static int _homeIndex = -1;
    private static long _stageStarted;
    private static long _readyQpc;
    private static long _interactionStart;
    private static long _promptStart;
    private static long _promptTicks;
    private static long _nextDiscoveryQpc;
    private static long _readinessObservationTicks;
    private static int _readinessObservations;
    private static bool _promptVisible;
    private static bool _fillNickname;
    private static bool _acknowledgeWarning;
    private static bool _warningVisible;
    private static long _warningStart;
    private static long _warningTicks;
    private static int _screenWidth;
    private static int _screenHeight;
    private static long _displayStableSince;
    private static string _graphicsBackend = string.Empty;
    private static bool _previousRunInBackground;
    private static bool _runInBackgroundCaptured;
    private static bool _quit;
    private static bool _failed;
    private static bool _completed;
    private static bool _corePresent;
    private static bool _modsVerified;
    private static string _menuScreenshot = string.Empty;
    private static string _settingsScreenshot = string.Empty;
    private static string _returnScreenshot = string.Empty;
    private static Type? _promptType;
    private static Type? _authType;
    private static string? _lastReadiness;
    private static bool _sceneObjectsDiscovered;
    private static bool _menuSceneInitialized;
    private static readonly HashSet<nint> InitializedWarnings = new();
    private static HarmonyLib.Harmony? _warningPatch;

    internal static void Initialize()
    {
        _fillNickname = Environment.GetCommandLineArgs().Contains("--anomaly-profile-fill-nickname", StringComparer.Ordinal);
        _acknowledgeWarning = Environment.GetCommandLineArgs().Contains("--anomaly-profile-acknowledge-warning", StringComparer.Ordinal);
        _quit = Environment.GetCommandLineArgs().Contains("--anomaly-profile-quit", StringComparer.Ordinal);
        _stageStarted = Stopwatch.GetTimestamp();
        _previousRunInBackground = Application.runInBackground;
        _runInBackgroundCaptured = true;
        Application.runInBackground = true;
        InstallWarningObservation();
        _menuScreenshot = Path.Combine(Core.OutputDirectory, "menu-ready.png");
        _settingsScreenshot = Path.Combine(Core.OutputDirectory, "settings-verified.png");
        _returnScreenshot = Path.Combine(Core.OutputDirectory, "menu-returned.png");
        Core.Mark("game.probe.ready", new { fillNickname = _fillNickname, acknowledgeWarning = _acknowledgeWarning,
            runInBackground = true, previousRunInBackground = _previousRunInBackground, quit = _quit });
    }

    internal static void Update()
    {
        if (_completed || _failed || Core.IsCompleted)
        {
            if (_quit && SecondsSince(_stageStarted) >= 1)
            {
                _quit = false;
                Application.Quit();
            }
            return;
        }

        try
        {
            _frame++;
            var updateQpc = Stopwatch.GetTimestamp();
            _updateIntervalTicks = _lastUpdateQpc == 0 ? long.MaxValue : updateQpc - _lastUpdateQpc;
            _lastUpdateQpc = updateQpc;
            ObserveDisplay();
            if (_frame == 1 || _frame % 30 == 0) ResolveAnomalyTypes();
            var hasPrompt = ObservePrompt();
            if (_stage == 0)
            {
                var observationStart = Stopwatch.GetTimestamp();
                ObserveReadiness(hasPrompt);
                _readinessObservationTicks += Stopwatch.GetTimestamp() - observationStart;
                _readinessObservations++;
                return;
            }

            if (hasPrompt || HasVisibleWarning())
                throw new InvalidOperationException("A modal appeared during menu verification.");
            if (SecondsSince(_stageStarted) > 20)
                throw new TimeoutException("Menu verification stage " + _stage + " did not finish within 20 seconds.");

            switch (_stage)
            {
                case 1:
                    if (!ScreenshotReady(_menuScreenshot)) return;
                    _interactionStart = Core.BeginSpan("menu.settings.first_use");
                    Click(_settingsButton!, "settings.open");
                    NextStage(2);
                    break;
                case 2:
                    _categories = FindActiveCategories();
                    if (_categories == null) return;
                    Core.Mark("menu.settings.opened", new { controller = _categories.gameObject.name, categoryCount = _categories._categories?.Length ?? 0 });
                    if (_corePresent)
                    {
                        var mods = FindModsCategory(_categories);
                        if (mods == null) throw new InvalidOperationException("Anomaly is loaded, but the Mods category was not installed.");
                        ExperimentHooks.BeforeSettingsOpen();
                        Click(mods.Activator, "settings.mods.select");
                    }
                    NextStage(3);
                    break;
                case 3:
                    if (_categories == null || !_categories.gameObject.activeInHierarchy) return;
                    if (_corePresent)
                    {
                        var mods = FindModsCategory(_categories);
                        if (mods?.Group == null || !mods.Group.activeInHierarchy) return;
                        _modsVerified = true;
                    }
                    if (SecondsSince(_stageStarted) < 0.35) return;
                    Core.EndSpan("menu.settings.first_use", _interactionStart, new { modsVerified = _modsVerified });
                    Capture(_settingsScreenshot, "settings.screenshot.requested");
                    NextStage(4);
                    break;
                case 4:
                    if (!ScreenshotReady(_settingsScreenshot)) return;
                    var back = FindSceneButton("Back", activeOnly: true);
                    if (back != null) Click(back, "settings.back");
                    else
                    {
                        // The normal tabs remain present in this menu. A raycast
                        // must still reach Settings before invoking menu navigation.
                        string? blocker = "missing Settings button";
                        if (_settingsButton == null || !CanRaycast(_settingsButton, out _, out blocker))
                            throw new InvalidOperationException("Return navigation is blocked by " + blocker);
                        Core.Mark("settings.back.api", new { menuIndex = _homeIndex, raycastVerified = true });
                        _menu!.ChangeMenu(_homeIndex);
                    }
                    NextStage(5);
                    break;
                case 5:
                    if (_settingsButton == null || !_settingsButton.gameObject.activeInHierarchy || !_settingsButton.IsInteractable()) return;
                    if (_categories != null && _categories.gameObject.activeInHierarchy) return;
                    if (SecondsSince(_stageStarted) < 0.2) return;
                    Capture(_returnScreenshot, "menu.return.screenshot.requested");
                    NextStage(6);
                    break;
                case 6:
                    if (!ScreenshotReady(_returnScreenshot)) return;
                    _completed = true;
                    _stageStarted = Stopwatch.GetTimestamp();
                    Core.Complete(new
                    {
                        criterion = "NewMainMenu active; loading and stock/Anomaly prompts absent; stable resolution; Settings reachable by EventSystem raycast for three consecutive frame intervals at most 100ms",
                        scene = SceneManager.GetActiveScene().name,
                        readyQpc = _readyQpc,
                        excludedPromptMilliseconds = _promptTicks * 1000d / Stopwatch.Frequency,
                        stockWarningMilliseconds = _warningTicks * 1000d / Stopwatch.Frequency,
                        display = new { width = _screenWidth, height = _screenHeight, backend = _graphicsBackend },
                        readinessObservationMilliseconds = _readinessObservationTicks * 1000d / Stopwatch.Frequency,
                        readinessObservationCount = _readinessObservations,
                        stableFrames = 3,
                        maxAcceptedFrameIntervalMilliseconds = _readyMaxIntervalTicks * 1000d / Stopwatch.Frequency,
                        frameIntervalThresholdMilliseconds = ResponsiveFrameThresholdMilliseconds,
                        interaction = "Stock Button.OnPointerClick, verified Settings activation and menu return",
                        settingsVerified = true,
                        modsRequired = _corePresent,
                        modsVerified = _modsVerified,
                        screenshots = new[] { _menuScreenshot, _settingsScreenshot, _returnScreenshot },
                    });
                    break;
            }
        }
        catch (Exception exception)
        {
            _failed = true;
            _stageStarted = Stopwatch.GetTimestamp();
            Core.Fail("Game probe: " + exception);
        }
    }

    private static void ObserveReadiness(bool hasPrompt)
    {
        // Awake/sceneLoaded can expose serialized UI before Unity has run Start.
        // In particular the warning's intended viewing time is still zero then.
        if (!_menuSceneInitialized) { ResetReadyFrames(); return; }
        if (_menu == null) _menu = NewMainMenu.Singleton;
        if (_menu == null || !_menu.gameObject.activeInHierarchy || _menu.gameObject.scene.name != "NewMainMenu")
        {
            ResetReadyFrames();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if ((!_sceneObjectsDiscovered || _settingsButton == null) && now >= _nextDiscoveryQpc)
        {
            _nextDiscoveryQpc = now + Stopwatch.Frequency / 10;
            _settingsButton = FindSceneButton("SettingsButton", activeOnly: true) ?? FindSceneButton("Settings", activeOnly: true);
            _loadingScreens = Resources.FindObjectsOfTypeAll<LoadingScreen>().ToArray();
            _warnings = Resources.FindObjectsOfTypeAll<PhotosensitivityWarning>().ToArray();
            _sceneObjectsDiscovered = true;
        }
        var stockWarning = ObserveWarning();
        var events = EventSystem.current;
        var eventReady = events != null && events.isActiveAndEnabled && events.currentInputModule != null && events.currentInputModule.isActiveAndEnabled;
        var controlsReady = _settingsButton != null && _settingsButton.gameObject.activeInHierarchy && _settingsButton.IsInteractable();
        string? raycastHit = null;
        var displayStable = SecondsSince(_displayStableSince) >= 0.5;
        var responsiveFrame = _updateIntervalTicks > 0
            && _updateIntervalTicks * 1000d / Stopwatch.Frequency <= ResponsiveFrameThresholdMilliseconds;
        var loading = _loadingScreens.Any(screen => screen != null && screen.root != null && screen.root.activeInHierarchy
            && (screen.selfGroup == null || screen.selfGroup.alpha > 0.01f));
        var reachable = controlsReady && eventReady && !loading && !hasPrompt && !stockWarning && displayStable
            && CanRaycast(_settingsButton!, out _, out raycastHit);
        var state = $"controls={controlsReady},events={eventReady},loading={loading},prompt={hasPrompt},warning={stockWarning},reachable={reachable},display={displayStable},responsive={responsiveFrame}";
        if (_lastReadiness != state)
        {
            _lastReadiness = state;
            Core.Mark("menu.readiness.state", new { controlsReady, eventReady, loading, hasPrompt, stockWarning, reachable, raycastHit, displayStable, responsiveFrame });
        }
        if (!controlsReady || !eventReady || loading || hasPrompt || stockWarning || !reachable || !displayStable || !responsiveFrame)
        {
            ResetReadyFrames();
            return;
        }
        _readyMaxIntervalTicks = Math.Max(_readyMaxIntervalTicks, _updateIntervalTicks);
        if (++_readyFrames < 3) return;

        if (_menu.submenus != null)
            for (var i = 0; i < _menu.submenus.Length; i++)
                if (_menu.submenus[i] != null && _menu.submenus[i].activeSelf) { _homeIndex = i; break; }
        if (_homeIndex < 0) _homeIndex = 0;
        _readyQpc = Stopwatch.GetTimestamp();
        Core.Mark("menu.ready", new { stableFrames = _readyFrames, settingsControl = _settingsButton!.gameObject.name,
            maxAcceptedFrameIntervalMilliseconds = _readyMaxIntervalTicks * 1000d / Stopwatch.Frequency,
            frameIntervalThresholdMilliseconds = ResponsiveFrameThresholdMilliseconds,
            raycastVerified = true, display = new { width = _screenWidth, height = _screenHeight, backend = _graphicsBackend },
            excludedPromptMilliseconds = _promptTicks * 1000d / Stopwatch.Frequency,
            readinessObservationMilliseconds = _readinessObservationTicks * 1000d / Stopwatch.Frequency,
            readinessObservationCount = _readinessObservations });
        ExperimentHooks.AfterMenuReady();
        Capture(_menuScreenshot, "menu.screenshot.requested");
        NextStage(1);
    }

    private static void ResolveAnomalyTypes()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Anomaly.Client");
        _corePresent = assembly != null;
        _promptType ??= assembly?.GetType("Anomaly.Client.Components.PasswordPrompt", false);
        _authType ??= assembly?.GetType("Anomaly.Client.Auth.AuthHandler", false);
    }

    private static bool ObservePrompt()
    {
        var instance = _promptType?.GetField("_instance", StaticMembers)?.GetValue(null);
        var visible = instance != null && instance.GetType().GetField("_isVisible", PrivateInstance)?.GetValue(instance) is true;
        if (visible != _promptVisible)
        {
            _promptVisible = visible;
            if (visible)
            {
                _promptStart = Stopwatch.GetTimestamp();
                Core.Mark("menu.prompt.open", new { autoFillFixture = _fillNickname });
            }
            else
            {
                var elapsed = Stopwatch.GetTimestamp() - _promptStart;
                _promptTicks += elapsed;
                Core.Mark("menu.prompt.closed", new { elapsedMilliseconds = elapsed * 1000d / Stopwatch.Frequency });
            }
        }
        if (!visible || !_fillNickname || instance == null || _stage != 0) return visible;

        var profile = _authType?.GetProperty("PlayerAuth", StaticMembers)?.GetValue(null)
            ?? _authType?.GetField("PlayerAuth", StaticMembers)?.GetValue(null);
        var username = profile?.GetType().GetProperty("Username")?.GetValue(profile) as string
            ?? profile?.GetType().GetField("Username")?.GetValue(profile) as string;
        var type = instance.GetType();
        var input = type.GetField("_input", PrivateInstance)?.GetValue(instance) as TMP_InputField;
        var validator = type.GetField("_validateAndSubmit", PrivateInstance)?.GetValue(instance);
        if (profile == null || !string.IsNullOrWhiteSpace(username) || input == null || !input.gameObject.activeInHierarchy
            || input.contentType == TMP_InputField.ContentType.Password || validator == null)
            return true;

        // Invoke the existing validated save callback through the existing prompt.
        // Never write a profile, key, consent or trust record directly.
        _fillNickname = false;
        input.SetTextWithoutNotify("LaunchProfileFixture");
        type.GetMethod("Submit", PrivateInstance)?.Invoke(instance, null);
        Core.Mark("menu.prompt.fixture_submitted");
        return true;
    }

    private static Button? FindSceneButton(string name, bool activeOnly)
    {
        foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (button == null || button.gameObject.scene.name != "NewMainMenu") continue;
            if (activeOnly && (!button.gameObject.activeInHierarchy || !button.IsInteractable())) continue;
            if (string.Equals(button.gameObject.name, name, StringComparison.OrdinalIgnoreCase)) return button;
        }
        return null;
    }

    private static UserSettingsCategories? FindActiveCategories()
    {
        foreach (var categories in Resources.FindObjectsOfTypeAll<UserSettingsCategories>())
            if (categories != null && categories.gameObject.scene.name == "NewMainMenu" && categories.gameObject.activeInHierarchy)
                return categories;
        return null;
    }

    private static UserSettingsCategories.Category? FindModsCategory(UserSettingsCategories categories)
    {
        if (categories._categories == null) return null;
        foreach (var category in categories._categories)
            if (category?.Group != null && category.Group.name == "ModsGroup") return category;
        return null;
    }

    private static void Click(Button button, string name)
    {
        if (button == null || !button.isActiveAndEnabled || !button.IsInteractable())
            throw new InvalidOperationException("UI control is unavailable for " + name);
        var events = EventSystem.current ?? throw new InvalidOperationException("EventSystem is unavailable.");
        if (!CanRaycast(button, out var position, out var blocker))
            throw new InvalidOperationException("UI control " + button.gameObject.name + " is blocked by " + blocker);
        events.SetSelectedGameObject(button.gameObject);
        Core.Mark(name + ".click", new { target = button.gameObject.name, dispatch = "Button.OnPointerClick", raycastVerified = true, x = position.x, y = position.y });
        button.OnPointerClick(new PointerEventData(events) { button = PointerEventData.InputButton.Left, position = position });
    }

    private static bool CanRaycast(Button button, out Vector2 position, out string? firstHit)
    {
        position = default;
        firstHit = null;
        var events = EventSystem.current;
        var rect = button.GetComponent<RectTransform>();
        var canvas = button.GetComponentInParent<Canvas>();
        if (events == null || rect == null || canvas == null) return false;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        if (position.x < 0 || position.y < 0 || position.x >= Screen.width || position.y >= Screen.height) return false;
        events.RaycastAll(new PointerEventData(events) { position = position }, RaycastResults);
        if (RaycastResults.Count == 0) return false;
        var hit = RaycastResults[0].gameObject;
        if (hit == null) return false;
        firstHit = hit.name;
        return hit == button.gameObject || hit.transform.IsChildOf(button.transform);
    }

    private static void ObserveDisplay()
    {
        var width = Screen.width;
        var height = Screen.height;
        if (width == _screenWidth && height == _screenHeight) return;
        _screenWidth = width;
        _screenHeight = height;
        _displayStableSince = Stopwatch.GetTimestamp();
        _graphicsBackend = SystemInfo.graphicsDeviceType.ToString();
        Core.Mark("display.changed", new { width, height, backend = _graphicsBackend });
        _readyFrames = 0;
        if (_stage > 0) throw new InvalidOperationException("Display dimensions changed after menu.ready; the run is not comparable.");
    }

    private static bool HasVisibleWarning() => _warnings.Any(w => w != null && w.gameObject.activeInHierarchy && w._parentObject != null && w._parentObject.activeInHierarchy);

    private static bool ObserveWarning()
    {
        var visible = HasVisibleWarning();
        if (visible != _warningVisible)
        {
            _warningVisible = visible;
            if (visible)
            {
                _warningStart = Stopwatch.GetTimestamp();
                Core.Mark("menu.stock_warning.open", new { autoAcknowledgeFixture = _acknowledgeWarning });
            }
            else
            {
                var elapsed = Stopwatch.GetTimestamp() - _warningStart;
                _warningTicks += elapsed;
                Core.Mark("menu.stock_warning.closed", new { elapsedMilliseconds = elapsed * 1000d / Stopwatch.Frequency });
            }
        }
        if (visible && _acknowledgeWarning)
        {
            foreach (var warning in _warnings)
            {
                if (warning == null || !warning.gameObject.activeInHierarchy || warning._parentObject == null || !warning._parentObject.activeInHierarchy) continue;
                // Stock Skip also enforces this countdown. Do not alter display
                // time, the warning preference, or its lifecycle.
                var startObserved = InitializedWarnings.Contains(warning.Pointer);
                if (!ReadinessPolicy.CanAcknowledgeWarning(startObserved, _menuSceneInitialized,
                    warning._currentViewingTime, warning._intendedViewingTime)) continue;
                Core.Mark("menu.stock_warning.fixture_acknowledge", new { viewingTime = warning._currentViewingTime,
                    intendedTime = warning._intendedViewingTime, startObserved, sceneInitialized = _menuSceneInitialized,
                    viewing = warning._intendedViewingTime > 0 ? "fresh" : "repeat" });
                warning.Skip();
            }
        }
        // Even an initially inactive serialized parent can be made active by
        // native Start. Never treat that pre-initialization state as ready.
        return visible || _warnings.Any(w => w != null && w.gameObject.activeInHierarchy && !InitializedWarnings.Contains(w.Pointer));
    }

    private static void InstallWarningObservation()
    {
        var started = Core.BeginSpan("probe.warning_start.install");
        var installed = false;
        try
        {
            var type = typeof(PhotosensitivityWarning);
            var target = AccessTools.DeclaredMethod(type, nameof(PhotosensitivityWarning.Start))
                ?? throw new MissingMethodException(type.FullName, nameof(PhotosensitivityWarning.Start));
            if (type.Assembly.CustomAttributes.Any(a => a.AttributeType.Name == "PatchShield")
                || type.CustomAttributes.Any(a => a.AttributeType.Name == "PatchShield")
                || target.CustomAttributes.Any(a => a.AttributeType.Name == "PatchShield"))
                throw new InvalidOperationException("PhotosensitivityWarning.Start is protected by PatchShield.");
            _warningPatch = new HarmonyLib.Harmony("Anomaly.LaunchProfiler.WarningStart");
            _warningPatch.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(GameProbe), nameof(WarningStarted))));
            installed = true;
        }
        finally { Core.EndSpan("probe.warning_start.install", started, new { installed, allModes = true }); }
    }

    private static void WarningStarted(PhotosensitivityWarning __instance)
    {
        try
        {
            InitializedWarnings.Add(__instance.Pointer);
            var intendedTime = __instance._intendedViewingTime;
            var visible = __instance._parentObject != null && __instance._parentObject.activeInHierarchy;
            Core.Mark("menu.stock_warning.initialized", new { startObserved = true, intendedTime, visible,
                viewing = !visible ? "suppressed_by_existing_preference" : intendedTime > 0 ? "fresh" : "repeat" });
        }
        catch (Exception exception)
        {
            // The observer must not replace an exception or interrupt stock Start.
            _failed = true;
            _stageStarted = Stopwatch.GetTimestamp();
            try { Core.Fail("Warning Start observation: " + exception); } catch { }
        }
    }

    internal static void HandleSceneInitialized(int buildIndex, string sceneName)
    {
        if (!string.Equals(sceneName, "NewMainMenu", StringComparison.Ordinal)) return;
        _menuSceneInitialized = true;
        _menu = null;
        _settingsButton = null;
        _sceneObjectsDiscovered = false;
        _nextDiscoveryQpc = 0;
        _readyFrames = 0;
        _loadingScreens = Array.Empty<LoadingScreen>();
        _warnings = Array.Empty<PhotosensitivityWarning>();
        Core.Mark("menu.probe.scene_initialized", new { buildIndex, cacheReset = true, warningRequiresObservedStart = true });
    }

    private static void Capture(string path, string mark)
    {
        Core.Mark(mark, new { path });
        MelonCoroutines.Start(CaptureAtEndOfFrame(path));
    }

    private static IEnumerator CaptureAtEndOfFrame(string path)
    {
        yield return new WaitForEndOfFrame();
        Texture2D? texture = null;
        try
        {
            // The supported generated string/PNG bindings contain invalid span
            // marshalling. Reuse the existing smoke tool's detached PNG encoder.
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, ScreenshotPng.Encode(texture.width, texture.height, texture.format, texture.GetRawTextureData().ToArray()));
            Core.Mark("screenshot.saved", new { path, width = texture.width, height = texture.height });
        }
        catch (Exception exception)
        {
            _failed = true;
            _stageStarted = Stopwatch.GetTimestamp();
            Core.Fail("Game probe screenshot: " + exception);
        }
        finally { if (texture != null) Object.Destroy(texture); }
    }

    private static bool ScreenshotReady(string path) => SecondsSince(_stageStarted) >= 0.2 && File.Exists(path) && new FileInfo(path).Length > 100;
    private static double SecondsSince(long ticks) => (Stopwatch.GetTimestamp() - ticks) / (double)Stopwatch.Frequency;
    private static void ResetReadyFrames() { _readyFrames = 0; _readyMaxIntervalTicks = 0; }
    private static void NextStage(int stage) { _stage = stage; _stageStarted = Stopwatch.GetTimestamp(); }
    internal static void Shutdown()
    {
        _warningPatch?.UnpatchSelf();
        _warningPatch = null;
        InitializedWarnings.Clear();
        if (_runInBackgroundCaptured)
        {
            Application.runInBackground = _previousRunInBackground;
            _runInBackgroundCaptured = false;
        }
        _menu = null;
        _settingsButton = null;
        _categories = null;
        _loadingScreens = Array.Empty<LoadingScreen>();
        _warnings = Array.Empty<PhotosensitivityWarning>();
        RaycastResults.Clear();
    }
}
