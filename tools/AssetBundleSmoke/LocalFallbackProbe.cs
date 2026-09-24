using System.Collections;
using System.Reflection;
using System.Text.Json;
using Anomaly.Client.Api.Assets;
using Anomaly.Shared.Assets;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Anomaly.AssetBundleSmoke;

internal sealed class LocalFallbackProbe
{
    private const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    private readonly string _output, _mode;
    private readonly Action<bool, string> _require;
    private readonly Type _manager = Type.GetType("Anomaly.Client.Assets.AssetBundleManager, Anomaly.Client", true);
    private readonly Type _adapter = Type.GetType("Anomaly.Client.Assets.NativeAssetBundle, Anomaly.Client", true);
    private bool _preloaderObserved;
    private double _readyAt;
    private int _stockLogo, _stockBackground, _stockMenu;

    internal LocalFallbackProbe(string output, string mode, Action<bool, string> require)
    {
        _output = output; _mode = mode; _require = require;
        _require(mode == "missing" || mode == "invalid", "Explicit local fallback test mode is recognized");
        CheckNoNativeAssets();
    }

    internal void ObserveScene(Scene scene)
    {
        if (_preloaderObserved || !scene.IsValid() || scene.name != "PreLoader") return;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            Image logo = null, background = null;
            for (int i = 0; i < canvas.transform.childCount; i++)
            {
                var child = canvas.transform.GetChild(i);
                if (child.name.Equals("image", StringComparison.OrdinalIgnoreCase)) logo = child.GetComponent<Image>();
                if (child.name.Equals("background", StringComparison.OrdinalIgnoreCase)) background = child.GetComponent<Image>();
            }
            if (logo?.sprite?.texture == null || background?.sprite?.texture == null) continue;
            _stockLogo = logo.sprite.texture.GetInstanceID();
            _stockBackground = background.sprite.texture.GetInstanceID();
            _preloaderObserved = true;
            _require(true, "Stock preloader logo/background remain available without native bundle loading");
        }
    }

    internal bool ReadyForCapture(bool menuReady)
    {
        ObserveScene(SceneManager.GetActiveScene());
        if (!menuReady || SceneManager.GetActiveScene().name != "NewMainMenu") return false;
        if (_readyAt == 0)
        {
            Screen.SetResolution(1280, 720, false);
            _readyAt = Time.realtimeSinceStartupAsDouble + 1;
            return false;
        }
        return Time.realtimeSinceStartupAsDouble >= _readyAt;
    }

    internal void Complete()
    {
        _require(_preloaderObserved, "Local fallback observed the real stock preloader");
        var menuLogo = UnityEngine.Object.FindObjectsOfType<RawImage>().FirstOrDefault(image => image != null && image.gameObject.activeInHierarchy && image.name == "Logo");
        _require(menuLogo != null && menuLogo.texture != null, "Stock main-menu logo remains available in local fallback mode");
        _stockMenu = menuLogo.texture.GetInstanceID();
        CheckNoNativeAssets();
        _manager.GetMethod("Shutdown", All).Invoke(null, null);
        CheckNoNativeAssets();
        File.WriteAllText(Path.Combine(_output, "local-fallback.json"), JsonSerializer.Serialize(new {
            status = "passed", mode = _mode, stockPreloader = true, stockMainMenu = true, rawSkinsIgnored = true,
            nativeLoadAttempts = 0, nativePacks = 0, cacheLeases = 0, stockLogo = _stockLogo,
            stockBackground = _stockBackground, stockMenu = _stockMenu,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void CheckNoNativeAssets()
    {
        _require((long)_adapter.GetProperty("LoadAttempts", All).GetValue(null) == 0, "Rejected or missing local packs cause zero native bundle load attempts");
        _require(((IDictionary)_manager.GetField("NativePacks", All).GetValue(null)).Count == 0, "No native pack remains in local fallback mode");
        var local = _manager.GetField("_local", All).GetValue(null);
        _require(((IDictionary)local.GetType().GetField("Assets", All).GetValue(local)).Count == 0, "The complete local address layer remains unpublished");
        var cache = (IDictionary)_manager.GetField("Cache", All).GetValue(null);
        int leases = 0;
        foreach (var copy in cache.Values) leases += (int)copy.GetType().GetField("Leases", All).GetValue(copy);
        _require(leases == 0, "Rejected local preparation releases every immutable cache lease");
        _require(!AnomalyResources.TryGetTexture(AssetAddress.Parse("game/skin/logo"), out _) &&
            !AnomalyResources.TryGetTexture(AssetAddress.Parse("game/skin/background"), out _), "Raw PNG skins never become typed replacement assets");
    }
}
