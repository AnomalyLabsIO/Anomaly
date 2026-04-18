using System;
using System.Collections;
using System.IO;
using Anomaly.Client.API.Features;
using Anomaly.Client.Assets;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Anomaly.Client.Internal.UI;

/// <summary>
/// Swaps the boot loading-screen's logo and background with user-provided PNGs
/// from <see cref="Paths.PreloaderSkinPath"/>. Fired from
/// <c>Core.OnSceneWasLoaded</c>; targets a Canvas in PreLoader/Loader whose
/// direct children are named "Background" and "Image" (the SCP:SL landing UI).
/// Retries for a few seconds because the target Canvas starts inactive and
/// gets enabled during async scene-load; the coroutine bails as soon as the
/// scene unloads or a non-target scene is dispatched.
/// </summary>
internal static class PreloaderSkinBootstrap
{
    private const string LogoFileName = "logo.png";
    private const string BackgroundFileName = "background.png";

    private const float RetryIntervalSec = 0.25f;
    private const int RetryAttempts = 6; // ~1.5 seconds — pass 0 typically suffices

    private static bool _hooked;
    private static string _lastDispatchName;
    private static float _lastDispatchRealtime;
    private static object _currentRoutine;

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

        // Dedup: OnSceneWasLoaded can double-fire within a frame.
        float now = Time.realtimeSinceStartup;
        if (_lastDispatchName == lowered && (now - _lastDispatchRealtime) < 1.0f)
            return;
        _lastDispatchName = lowered;
        _lastDispatchRealtime = now;

        // Any in-flight routine belongs to a previous scene — cancel it so we
        // don't keep polling the old scene's hierarchy while on the main menu.
        if (_currentRoutine != null)
        {
            MelonCoroutines.Stop(_currentRoutine);
            _currentRoutine = null;
        }

        if (!lowered.Contains("preloader") && lowered != "loader")
            return;

        try
        {
            StartSkinRoutine(scene);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"PreloaderSkinBootstrap: dispatch failed (scene='{name}'): {ex}");
        }
    }

    private static void StartSkinRoutine(Scene scene)
    {
        string logoPath = Path.Combine(Paths.PreloaderSkinPath, LogoFileName);
        string bgPath = Path.Combine(Paths.PreloaderSkinPath, BackgroundFileName);
        bool haveLogo = File.Exists(logoPath);
        bool haveBg = File.Exists(bgPath);
        if (!haveLogo && !haveBg) return;

        Texture2D logoTex = haveLogo ? LocalTextures.TryLoad(logoPath) : null;
        Texture2D bgTex = haveBg ? LocalTextures.TryLoad(bgPath) : null;

        Sprite logoSprite = logoTex != null ? NamedSprite(logoTex, "AnomalyLogo") : null;
        Sprite bgSprite = bgTex != null ? NamedSprite(bgTex, "AnomalyBackground") : null;

        _currentRoutine = MelonCoroutines.Start(SkinRoutine(scene, logoSprite, bgSprite));
    }

    private static IEnumerator SkinRoutine(Scene scene, Sprite logoSprite, Sprite bgSprite)
    {
        string expectedScene = scene.name;
        SwapPass(scene, logoSprite, bgSprite, pass: 0);

        for (int pass = 1; pass <= RetryAttempts; pass++)
        {
            yield return new WaitForSeconds(RetryIntervalSec);

            // Fail-safe: bail if we're no longer on the scene we started on.
            // Covers the case where MelonCoroutines.Stop doesn't catch us in
            // time on a scene transition.
            var active = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || active.name != expectedScene)
            {
                _currentRoutine = null;
                yield break;
            }

            SwapPass(scene, logoSprite, bgSprite, pass);
        }

        _currentRoutine = null;
    }

    private static void SwapPass(Scene scene, Sprite logoSprite, Sprite bgSprite, int pass)
    {
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
                if (bgChild == null || logoChild == null)
                    continue;

                canvasMatches++;
                if (bgSprite != null && bgChild.sprite != bgSprite)
                {
                    bgChild.sprite = bgSprite;
                    bgSwaps++;
                }
                if (logoSprite != null && logoChild.sprite != logoSprite)
                {
                    logoChild.sprite = logoSprite;
                    logoSwaps++;
                }
            }
        }

        if (pass == 0 || bgSwaps > 0 || logoSwaps > 0)
        {
            MelonLogger.Msg(
                $"PreloaderSkin[pass={pass}]: matched={canvasMatches} " +
                $"bg={bgSwaps} logo={logoSwaps} scene='{scene.name}'");
        }
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
