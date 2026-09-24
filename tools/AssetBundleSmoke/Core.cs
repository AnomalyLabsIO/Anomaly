using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Anomaly.EarlyAccessUiSmoke;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(Anomaly.AssetBundleSmoke.Core), "Anomaly AssetBundle Smoke", "1.0.0", "AnomalyLabs", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: MelonAdditionalDependencies("Anomaly.Client")]

namespace Anomaly.AssetBundleSmoke;

// Opt-in local fixture probe, deliberately absent from release packages. The
// supplied hash pins an owner-built fixture; this is not a remote bundle validator.
public sealed class Core : MelonMod
{
    private const BindingFlags AdapterFlags = BindingFlags.NonPublic | BindingFlags.Static;
    private string _output;
    private string _bundleHash;
    private object _bundle;
    private Texture2D _logo;
    private Texture2D _background;
    private Sprite _logoSprite;
    private Sprite _backgroundSprite;
    private Image _preloaderLogo;
    private Image _preloaderBackground;
    private readonly List<RawImage> _menuLogos = new();
    private Type _adapter;
    private bool _nativePassed;
    private bool _preloaderApplied;
    private bool _menuApplied;
    private bool _menuCaptureStarted;
    private bool _preloaderCaptured;
    private bool _menuCaptured;
    private bool _unloadPassed;
    private bool _finished;
    private double _deadline;
    private double _menuCaptureAt;
    private bool _menuInitialized;
    private bool _nicknameSubmitted;
    private bool _pipeline;
    private AdvancedProbe _advanced;
    private bool _advancedCaptureStarted;
    private NetworkProbe _network;
    private bool _networkCaptureStarted;
    private bool _networkPassed;
    private LocalFallbackProbe _fallback;
    private bool _fallbackCaptureStarted;

    public override void OnInitializeMelon()
    {
        _output = Environment.GetEnvironmentVariable("ANOMALY_ASSETBUNDLE_SMOKE_DIR");
        if (string.IsNullOrEmpty(_output)) return;
        try
        {
            _output = Path.GetFullPath(_output);
            string game = Path.GetFullPath(MelonEnvironment.GameRootDirectory);
            Require(Directory.Exists(_output), "Runner created evidence directory");
            Require(_output.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Evidence stays in the isolated game");
            string markerPath = Path.Combine(game, "assetbundle-smoke-staging.json");
            Require(File.Exists(markerPath), "Isolated game staging marker exists");
            using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
            Require(marker.RootElement.GetProperty("purpose").GetString() == "AssetBundleSmoke.v1" &&
                    string.Equals(marker.RootElement.GetProperty("destination").GetString(), game, StringComparison.OrdinalIgnoreCase),
                "Staging marker owns this game path");

            string appData = Path.Combine(game, "ProfileAppData");
            string config = Path.Combine(appData, "config");
            Require(Directory.Exists(appData) && Directory.Exists(config), "Private stock appdata and config exist");
            Il2Cpp.FileManager.SetAppFolder(appData);
            Il2Cpp.FileManager.SetConfigFolder(config);
            Require(Path.GetFullPath(Il2Cpp.FileManager.GetAppFolder(false, false, "")) == appData,
                "Stock FileManager uses private appdata");
            Require(Path.GetFullPath(Il2Cpp.FileManager.GetAppFolder(false, true, "")) == config,
                "Stock FileManager uses private config");
            string skins = Path.Combine(MelonEnvironment.UserDataDirectory, "Skins");
            string fallbackMode = Environment.GetEnvironmentVariable("ANOMALY_ASSETBUNDLE_SMOKE_LOCAL_FALLBACK");
            if (string.IsNullOrEmpty(fallbackMode))
                Require(!Directory.Exists(skins) || !Directory.EnumerateFiles(skins, "*", SearchOption.AllDirectories).Any(), "No raw skin files are present");
            else
                Require(File.Exists(Path.Combine(skins, "logo.png")) && File.Exists(Path.Combine(skins, "background.png")), "Fallback fixture contains both ignored raw PNG skins");
            Require(!Directory.EnumerateFiles(Path.Combine(game, "UserLibs"), "*.dll")
                .Any(file => Path.GetFileName(file).Equals("BigGustave.dll", StringComparison.OrdinalIgnoreCase) ||
                             Path.GetFileName(file).Equals("NVorbis.dll", StringComparison.OrdinalIgnoreCase)),
                "Private runtime contains no PNG or OGG decoder libraries");

            string path = Path.GetFullPath(RequiredEnvironment("ANOMALY_ASSETBUNDLE_SMOKE_BUNDLE"));
            Require(path.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Fixture is staged inside the isolated game");
            string expected = RequiredEnvironment("ANOMALY_ASSETBUNDLE_SMOKE_SHA256");
            Require(expected.Length == 64 && expected.All(Uri.IsHexDigit), "Expected fixture SHA256 is well formed");
            _bundleHash = HashFile(path);
            Require(string.Equals(expected, _bundleHash, StringComparison.OrdinalIgnoreCase), "Owner-built fixture hash matches");

            _adapter = Type.GetType("Anomaly.Client.Assets.NativeAssetBundle, Anomaly.Client", throwOnError: true);
            Log("adapter", new { assembly = _adapter.Assembly.Location, sha256 = HashFile(_adapter.Assembly.Location) });
            _pipeline = Environment.GetEnvironmentVariable("ANOMALY_ASSETBUNDLE_SMOKE_PIPELINE") == "1";
            if (!string.IsNullOrEmpty(fallbackMode))
            {
                _fallback = new LocalFallbackProbe(_output, fallbackMode, Require);
                Application.runInBackground = true;
                _deadline = Time.realtimeSinceStartupAsDouble + 75;
                _fallback.ObserveScene(SceneManager.GetActiveScene());
                return;
            }
            var watch = Stopwatch.StartNew();
            if (!_pipeline)
            {
                _bundle = Invoke("LoadFromFile", new object[] { path, 0u });
                Require(_bundle != null, "Native adapter loaded the pinned bundle");
            }
            _logo = LoadTexture(RequiredEnvironment("ANOMALY_ASSETBUNDLE_SMOKE_LOGO"), 2048, 2048);
            _background = LoadTexture(RequiredEnvironment("ANOMALY_ASSETBUNDLE_SMOKE_BACKGROUND"), 3328, 1872);
            Require(LoadTexture(RequiredEnvironment("ANOMALY_ASSETBUNDLE_SMOKE_LOGO"), 2048, 2048).GetInstanceID() == _logo.GetInstanceID(),
                "Repeated asset load returns the retained texture");
            _nativePassed = true;
            Log("native-loaded", new { elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, unity = Application.unityVersion,
                platform = Application.platform.ToString(), logo = _logo.GetInstanceID(), background = _background.GetInstanceID() });
            WriteResult("native-loaded", null);
            Application.runInBackground = true;
            _deadline = Time.realtimeSinceStartupAsDouble + 75;
            TryApplyPreloader(SceneManager.GetActiveScene());
        }
        catch (Exception ex) { Fail(ex); }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if ((!_nativePassed && _fallback == null) || _finished) return;
        try
        {
            Log("scene", new { buildIndex, sceneName });
            if (_fallback != null) { _fallback.ObserveScene(SceneManager.GetSceneByName(sceneName)); return; }
            TryApplyPreloader(SceneManager.GetSceneByName(sceneName));
        }
        catch (Exception ex) { Fail(ex); }
    }

    public override void OnUpdate()
    {
        if ((!_nativePassed && _fallback == null) || _finished) return;
        try
        {
            DismissNickname();
            if (_fallback != null)
            {
                if (_fallback.ReadyForCapture(_menuInitialized && MenuPromptsCleared()) && !_fallbackCaptureStarted)
                {
                    _fallbackCaptureStarted = true;
                    MelonCoroutines.Start(CaptureFallback());
                }
                if (Time.realtimeSinceStartupAsDouble >= _deadline) throw new TimeoutException("Local fallback observation timed out.");
                return;
            }
            if (_network != null)
            {
                _network.Update();
                if (_network.NeedsCapture && !_networkCaptureStarted)
                {
                    _networkCaptureStarted = true;
                    MelonCoroutines.Start(Capture("network-assets", finish: false));
                }
                if (_network.Complete)
                {
                    UnloadFixture();
                    _network.VerifyFinalShutdown();
                    _networkPassed = true;
                    Finish("passed", null);
                    return;
                }
            }
            if (_advanced != null)
            {
                _advanced.Update();
                if (_advanced.NeedsCapture && !_advancedCaptureStarted)
                {
                    _advancedCaptureStarted = true;
                    MelonCoroutines.Start(Capture("qualified-assets", finish: false));
                }
            }
            if (!_preloaderApplied) TryApplyPreloader(SceneManager.GetActiveScene());
            if (!_menuApplied && (!_pipeline || _menuInitialized) && string.Equals(SceneManager.GetActiveScene().name, "NewMainMenu", StringComparison.OrdinalIgnoreCase))
                TryApplyMenu();
            if (_menuApplied && !_menuCaptureStarted && Time.realtimeSinceStartupAsDouble >= _menuCaptureAt && MenuPromptsCleared())
            {
                _menuCaptureStarted = true;
                MelonCoroutines.Start(Capture("main-menu", finish: true));
            }
            if (Time.realtimeSinceStartupAsDouble >= _deadline)
            {
                UnloadFixture();
                Finish("native-pass-display-incomplete", "The native texture gate passed, but a menu frame was not captured before the deadline.");
            }
        }
        catch (Exception ex) { Fail(ex); }
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (sceneName == "NewMainMenu") _menuInitialized = true;
    }

    private bool MenuPromptsCleared()
    {
        if (!_menuInitialized) return false;
        if (!DismissNickname()) return false;
        bool warningVisible = false;
        foreach (var warning in Resources.FindObjectsOfTypeAll<Il2Cpp.PhotosensitivityWarning>())
        {
            if (warning == null || !warning.gameObject.activeInHierarchy || warning._parentObject == null || !warning._parentObject.activeInHierarchy) continue;
            warningVisible = true;
            if (warning._intendedViewingTime > 0 && warning._currentViewingTime >= warning._intendedViewingTime)
            {
                Log("stock-warning-acknowledged", new { current = warning._currentViewingTime, intended = warning._intendedViewingTime });
                warning.Skip();
            }
        }
        return !warningVisible;
    }

    private bool DismissNickname()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var prompt = Type.GetType("Anomaly.Client.Components.PasswordPrompt, Anomaly.Client", true);
        var instance = prompt.GetField("_instance", all).GetValue(null);
        if (instance != null && (bool)instance.GetType().GetField("_isVisible", all).GetValue(instance))
        {
            var input = (Il2CppTMPro.TMP_InputField)instance.GetType().GetField("_input", all).GetValue(instance);
            if (!_nicknameSubmitted && input != null && input.gameObject.activeInHierarchy)
            {
                input.SetTextWithoutNotify("AssetBundle Smoke");
                instance.GetType().GetMethod("Submit", all).Invoke(instance, null);
                _nicknameSubmitted = true;
                Log("private-nickname-submitted", new { });
            }
            return false;
        }
        return true;
    }

    private Texture2D LoadTexture(string name, int width, int height)
    {
        Texture2D texture;
        if (_pipeline)
        {
            var manager = Type.GetType("Anomaly.Client.Assets.AssetBundleManager, Anomaly.Client", true);
            object[] arguments = { name, null };
            Require((bool)manager.GetMethod("TryGetLocalTexture", AdapterFlags).Invoke(null, arguments), "Production local pack resolver contains " + name);
            texture = (Texture2D)arguments[1];
        }
        else
        {
            MethodInfo loader = _adapter.GetMethod("LoadAsset", AdapterFlags).MakeGenericMethod(typeof(Texture2D));
            texture = (Texture2D)loader.Invoke(null, new object[] { _bundle, name });
        }
        Require(texture != null, "Named Texture2D loaded: " + name);
        Require(texture.width == width && texture.height == height, "Exact texture dimensions: " + name);
        Log("texture", new { name, width = texture.width, height = texture.height, format = texture.format.ToString(), textureName = texture.name });
        return texture;
    }

    private void TryApplyPreloader(Scene scene)
    {
        if (_preloaderApplied || !scene.IsValid() || !string.Equals(scene.name, "PreLoader", StringComparison.OrdinalIgnoreCase)) return;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            Image logoTarget = FindDirectImage(canvas.transform, "image");
            Image backgroundTarget = FindDirectImage(canvas.transform, "background");
            if (logoTarget == null || backgroundTarget == null) continue;
            if (!_pipeline)
            {
                _logoSprite ??= Sprite.Create(_logo, new Rect(0, 0, _logo.width, _logo.height), new Vector2(0.5f, 0.5f));
                _backgroundSprite ??= Sprite.Create(_background, new Rect(0, 0, _background.width, _background.height), new Vector2(0.5f, 0.5f));
                logoTarget.sprite = _logoSprite;
                backgroundTarget.sprite = _backgroundSprite;
            }
            Require(logoTarget.sprite != null && backgroundTarget.sprite != null && logoTarget.sprite.texture.GetInstanceID() == _logo.GetInstanceID() &&
                    backgroundTarget.sprite.texture.GetInstanceID() == _background.GetInstanceID(), "Real preloader Images reference bundle textures");
            _preloaderApplied = true;
            _preloaderLogo = logoTarget;
            _preloaderBackground = backgroundTarget;
            Log("preloader-applied", new { scene = scene.name, canvas = canvas.name });
            MelonCoroutines.Start(Capture("preloader", finish: false));
            return;
        }
    }

    private static Image FindDirectImage(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
            {
                var image = child.GetComponent<Image>();
                if (image != null) return image;
            }
        }
        return null;
    }

    private void TryApplyMenu()
    {
        Require(_logo != null && _background != null, "Bundle textures survive the preloader scene transition");
        int swapped = 0;
        foreach (var target in UnityEngine.Object.FindObjectsOfType<RawImage>())
        {
            if (target == null || !target.gameObject.activeInHierarchy || target.gameObject.name != "Logo") continue;
            if (!_pipeline) target.texture = _logo;
            Require(target.texture != null && target.texture.m_CachedPtr != IntPtr.Zero &&
                target.texture.GetInstanceID() == _logo.GetInstanceID(), "Real menu Logo RawImage references live bundle texture");
            _menuLogos.Add(target);
            swapped++;
        }
        if (swapped == 0) return;
        _menuApplied = true;
        _menuCaptureAt = Time.realtimeSinceStartupAsDouble + 3;
        Log("menu-applied", new { swapped });
        LogAssetLifetime("menu-applied-asset-lifetime");
    }

    private IEnumerator Capture(string name, bool finish)
    {
        // Initialization can stall before the first rendered frame. Let the
        // stock fade advance naturally after that frame, without holding scenes.
        yield return null;
        double visibleAfter = Time.realtimeSinceStartupAsDouble + (name == "preloader" ? 0.75 : 0.5);
        while (Time.realtimeSinceStartupAsDouble < visibleAfter) yield return null;
        if (name == "preloader")
        {
            while (SceneManager.GetActiveScene().name == "PreLoader" &&
                (VisibleAlpha(_preloaderLogo) < 0.5f || VisibleAlpha(_preloaderBackground) < 0.5f)) yield return null;
            Log("preloader-visibility", new { logo = VisibleAlpha(_preloaderLogo), background = VisibleAlpha(_preloaderBackground) });
        }
        yield return new WaitForEndOfFrame();
        Texture2D frame = null;
        try
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if (name == "preloader" && !string.Equals(sceneName, "PreLoader", StringComparison.OrdinalIgnoreCase))
            {
                Log("capture-missed", new { name, scene = sceneName });
                yield break;
            }
            Require(_logo != null && _background != null, "Bundle textures are alive at capture: " + name);
            if (name == "preloader")
                Require(_preloaderLogo != null && _preloaderBackground != null &&
                    _preloaderLogo.sprite != null && _preloaderBackground.sprite != null &&
                    _preloaderLogo.sprite.texture != null && _preloaderBackground.sprite.texture != null &&
                    _preloaderLogo.sprite.texture.GetInstanceID() == _logo.GetInstanceID() &&
                    _preloaderBackground.sprite.texture.GetInstanceID() == _background.GetInstanceID(),
                    "Preloader bindings still reference live bundle textures at capture");
            if (name == "main-menu")
                Require(_menuLogos.Count > 0 && _menuLogos.All(target => target != null && target.gameObject.activeInHierarchy &&
                    target.texture != null && target.texture.GetInstanceID() == _logo.GetInstanceID()),
                    "Menu bindings still reference live bundle texture at capture");
            frame = ScreenCapture.CaptureScreenshotAsTexture();
            Require(frame != null, "Engine screenshot available: " + name);
            string path = Path.Combine(_output, name + ".png");
            File.WriteAllBytes(path, ScreenshotPng.Encode(frame.width, frame.height, frame.format, frame.GetRawTextureData().ToArray()));
            Log("capture", new { name, frame.width, frame.height, scene = SceneManager.GetActiveScene().name });
            if (name == "qualified-assets")
            {
                _advanced.VerifyVisualFrame(frame);
                _advanced.CaptureCompleted();
                Require(_advanced.Complete, "All native asset and lifecycle qualification steps passed");
                UnloadFixture();
                Finish("passed", null);
                yield break;
            }
            if (name == "network-assets")
            {
                _network.CaptureCompleted();
                _networkCaptureStarted = false;
                yield break;
            }
            if (name == "preloader") _preloaderCaptured = true;
            if (name == "main-menu") _menuCaptured = true;
            if (finish)
            {
                if (Environment.GetEnvironmentVariable("ANOMALY_ASSETBUNDLE_SMOKE_NETWORK") == "1")
                {
                    _network = new NetworkProbe(_output, Require, Log);
                    Screen.SetResolution(1280, 720, false);
                    _deadline = Time.realtimeSinceStartupAsDouble + 540;
                    yield break;
                }
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANOMALY_ASSETBUNDLE_QUALIFICATION_INDEX")))
                {
                    _advanced = new AdvancedProbe(_output, Require, Log);
                    _deadline = Time.realtimeSinceStartupAsDouble + 45;
                    yield break;
                }
                UnloadFixture();
                bool complete = _nativePassed && _preloaderApplied && _preloaderCaptured && _menuApplied && _menuCaptured && _unloadPassed;
                Finish(complete ? "passed" : "native-pass-display-incomplete",
                    complete ? null : "Native loading passed, but all three real UI bindings and their scene frames were not witnessed.");
            }
        }
        catch (Exception ex) { Fail(ex); }
        finally { if (frame != null) UnityEngine.Object.Destroy(frame); }
    }

    private void UnloadFixture()
    {
        if (_pipeline && !_unloadPassed)
        {
            Type.GetType("Anomaly.Client.Internal.UI.PreloaderSkinBootstrap, Anomaly.Client", true).GetMethod("RestoreAll", AdapterFlags).Invoke(null, null);
            Type.GetType("Anomaly.Client.Patches.Menu.MainMenuCustomizer, Anomaly.Client", true).GetMethod("RestoreLogo", AdapterFlags).Invoke(null, null);
            Type.GetType("Anomaly.Client.Assets.AssetBundleManager, Anomaly.Client", true).GetMethod("Shutdown", AdapterFlags).Invoke(null, null);
            Require(_logo == null && _background == null, "Production manager unloads the released local pack");
            _unloadPassed = true;
            return;
        }
        if (_bundle == null || _unloadPassed) return;
        LogAssetLifetime("before-unload-false");
        Invoke("Unload", new object[] { _bundle, false });
        _bundle = null;
        LogAssetLifetime("after-unload-false");
        Require(_logo != null && _background != null, "Unload(false) retains loaded texture objects");
        Require((_logo.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0 &&
            (_background.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0, "Unload(false) restores original asset retention flags");

        _bundle = Invoke("LoadFromFile", new object[] { RequiredEnvironment("ANOMALY_ASSETBUNDLE_SMOKE_BUNDLE"), 0u });
        Require(_bundle != null, "Bundle reload after Unload(false) succeeds");
        var secondLogo = LoadTexture(RequiredEnvironment("ANOMALY_ASSETBUNDLE_SMOKE_LOGO"), 2048, 2048);
        var secondBackground = LoadTexture(RequiredEnvironment("ANOMALY_ASSETBUNDLE_SMOKE_BACKGROUND"), 3328, 1872);
        Require(secondLogo.GetInstanceID() != _logo.GetInstanceID() && secondBackground.GetInstanceID() != _background.GetInstanceID(),
            "Reloaded bundle owns distinct texture objects");
        Invoke("Unload", new object[] { _bundle, true });
        _bundle = null;
        Require(secondLogo == null && secondBackground == null, "Unload(true) destroys the reloaded textures");
        Require(_logo != null && _background != null, "Unload(true) leaves earlier detached textures alive");
        Log("unload-modes-verified", new { falseRetainsAssets = true, trueDestroysAssets = true });
        _unloadPassed = true;
    }

    private IEnumerator CaptureFallback()
    {
        yield return new WaitForEndOfFrame();
        Texture2D frame = null;
        try
        {
            frame = ScreenCapture.CaptureScreenshotAsTexture();
            Require(frame != null, "Engine frame available for stock fallback");
            File.WriteAllBytes(Path.Combine(_output, "local-fallback.png"), ScreenshotPng.Encode(frame.width, frame.height, frame.format, frame.GetRawTextureData().ToArray()));
            _fallback.Complete();
            Finish("passed", null);
        }
        catch (Exception exception) { Fail(exception); }
        finally { if (frame != null) UnityEngine.Object.Destroy(frame); }
    }

    private void LogAssetLifetime(string stage) => Log(stage, new {
        logoAlive = _logo != null, logoPointer = ReferenceEquals(_logo, null) ? "null" : _logo.m_CachedPtr.ToString("x"),
        backgroundAlive = _background != null, backgroundPointer = ReferenceEquals(_background, null) ? "null" : _background.m_CachedPtr.ToString("x") });

    private static float VisibleAlpha(Image image)
    {
        if (image == null || !image.gameObject.activeInHierarchy || !image.enabled) return 0;
        float alpha = image.color.a;
        foreach (var group in image.GetComponentsInParent<CanvasGroup>()) alpha *= group.alpha;
        return alpha;
    }

    private object Invoke(string method, object[] arguments) => _adapter.GetMethod(method, AdapterFlags).Invoke(null, arguments);

    private static string RequiredEnvironment(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException("Missing opt-in environment: " + name);

    private static string HashFile(string path)
    {
        using var input = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(input)).ToLowerInvariant();
    }

    private void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        File.AppendAllText(Path.Combine(_output, "assertions.log"), "PASS " + message + "\n");
    }

    private void Log(string stage, object details) => File.AppendAllText(Path.Combine(_output, "witness.jsonl"),
        JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, stage, details }) + "\n");

    private void WriteResult(string status, string error) => File.WriteAllText(Path.Combine(_output, "native-gate.json"),
        JsonSerializer.Serialize(new { status, error, nativePassed = _nativePassed, bundleSha256 = _bundleHash,
            productionPipeline = _pipeline,
            preloaderApplied = _preloaderApplied, preloaderCaptured = _preloaderCaptured, menuApplied = _menuApplied,
            menuCaptured = _menuCaptured, unloadPassed = _unloadPassed, managedValidatorApplied = _pipeline,
            authenticatedTransferQualified = _networkPassed },
            new JsonSerializerOptions { WriteIndented = true }));

    private void Finish(string status, string error)
    {
        if (_finished) return;
        if (status == "passed" && _fallback == null && !(_nativePassed && _preloaderApplied && _preloaderCaptured && _menuApplied && _menuCaptured && _unloadPassed))
        {
            status = "native-pass-display-incomplete";
            error = "The complete native/UI/lifecycle witness is required for a full pass.";
        }
        _finished = true;
        WriteResult(status, error);
        MelonLogger.Msg("ASSETBUNDLE_NATIVE_GATE " + status);
        Application.Quit();
    }

    private void Fail(Exception ex)
    {
        Exception error = ex is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : ex;
        MelonLogger.Error(error.ToString());
        if (!string.IsNullOrEmpty(_output) && Directory.Exists(_output))
        {
            File.WriteAllText(Path.Combine(_output, "failure.txt"), error.ToString());
            Finish("failed", error.ToString());
        }
        else Application.Quit();
    }
}
