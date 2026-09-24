using System;
using Anomaly.Client.Assets;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Anomaly.Client.Internal.UI;

/// <summary>
/// Swaps the PreLoader scene's <c>Canvas/Background</c> and <c>Canvas/Image</c>
/// sprites with validated local pack textures. Server packs never affect skins.
/// </summary>
internal static class PreloaderSkinBootstrap
{
    private static bool _hooked;
    private static string _lastDispatchName;
    private static float _lastDispatchRealtime;
    private static readonly System.Collections.Generic.List<(Image Target, Sprite Original, Sprite Replacement)> Bindings = new();

    internal static void Init()
    {
        if (_hooked) return;
        _hooked = true;

        var active = SceneManager.GetActiveScene();
        if (active.IsValid())
            Dispatch(active);
    }

    internal static void HandleSceneWasLoaded(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        var scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid()) return;
        Dispatch(scene);
    }

    private static void Dispatch(Scene scene)
    {
        string name = scene.name ?? string.Empty;
        string lowered = name.ToLowerInvariant();

        float now = Time.realtimeSinceStartup;
        if (_lastDispatchName == lowered && (now - _lastDispatchRealtime) < 1.0f)
            return;
        _lastDispatchName = lowered;
        _lastDispatchRealtime = now;

        if (lowered != "preloader") return;

        try
        {
            ApplySkin(scene);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"PreloaderSkinBootstrap: apply failed: {ex}");
        }
    }

    private static void ApplySkin(Scene scene)
    {
        AssetBundleManager.TryGetLocalTexture("game/skin/logo", out var logoTex);
        AssetBundleManager.TryGetLocalTexture("game/skin/background", out var bgTex);
        if (logoTex == null && bgTex == null) return;

        Sprite logoSprite = logoTex != null ? NamedSprite(logoTex, "AnomalyLogo") : null;
        Sprite bgSprite = bgTex != null ? NamedSprite(bgTex, "AnomalyBackground") : null;

        int canvasMatches = 0;
        int bgSwaps = 0, logoSwaps = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == null) continue;
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas == null) continue;

                Image bgChild = FindDirectChildImage(canvas.transform, "background");
                Image logoChild = FindDirectChildImage(canvas.transform, "image");
                if (bgChild == null || logoChild == null) continue;

                canvasMatches++;
                if (bgSprite != null)
                {
                    AssetLifetime.Retain(bgChild.sprite);
                    Bindings.Add((bgChild, bgChild.sprite, bgSprite));
                    bgChild.sprite = bgSprite;
                    bgSwaps++;
                }
                if (logoSprite != null)
                {
                    AssetLifetime.Retain(logoChild.sprite);
                    Bindings.Add((logoChild, logoChild.sprite, logoSprite));
                    logoChild.sprite = logoSprite;
                    logoSwaps++;
                }
            }
        }

        MelonLogger.Msg($"PreloaderSkin: matched={canvasMatches} bg={bgSwaps} logo={logoSwaps}");
    }

    internal static void RestoreAll()
    {
        foreach (var binding in Bindings)
        {
            if (binding.Target != null && binding.Target.sprite == binding.Replacement) binding.Target.sprite = binding.Original;
            AssetLifetime.Release(binding.Original);
            if (binding.Replacement != null) UnityEngine.Object.Destroy(binding.Replacement);
        }
        Bindings.Clear();
    }

    private static Image FindDirectChildImage(Transform parent, string childNameLower)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child == null) continue;
            if (!string.Equals(child.name, childNameLower, StringComparison.OrdinalIgnoreCase))
                continue;
            var img = child.GetComponent<Image>();
            if (img != null) return img;
        }
        return null;
    }

    private static Sprite NamedSprite(Texture2D tex, string name)
    {
        var s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        s.name = name;
        return s;
    }
}
