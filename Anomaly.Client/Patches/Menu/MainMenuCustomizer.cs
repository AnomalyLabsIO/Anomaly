using System;
using System.IO;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Assets;
using Anomaly.Client.Auth;
using Anomaly.Client.Components;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using ClientFeature = Anomaly.Client.Api.Features.Client;

namespace Anomaly.Client.Patches.Menu;

/// <summary>
/// Brands the stock <c>NewMainMenu</c> scene: personalized welcome line,
/// version (MODDED) suffix, custom logo, authenticated-user auth status,
/// and removes the vestigial loading spinner. Subscribes to
/// <see cref="ClientEvents.CanvasReady"/> so it inherits the same timing
/// the rest of the UI bootstrap uses.
/// </summary>
internal static class MainMenuCustomizer
{
    private const string LogoFileName = "logo.png";
    private const string WelcomePath = "News/Welcome";
    private const string AuthLoadingPath = "Servers/Auth Status/Loading";
    private const string DefaultUsername = "Player";

    private static bool _hooked;

    internal static void Init()
    {
        if (_hooked) return;
        _hooked = true;

        ClientEvents.CanvasReady += OnCanvasReady;
    }

    private static void OnCanvasReady(CanvasReadyEventArgs args)
    {
        try
        {
            Apply(args.Canvas);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"MainMenuCustomizer: apply failed: {ex}");
        }
    }

    private static void Apply(GameObject canvasGo)
    {
        var menu = ClientFeature.MainMenu;

        TryApplyWelcome(menu);
        TryApplyVersion(menu);
        TryApplyLogo();
        TryApplyAuthStatus(menu);
        TryDisableAuthLoading(menu);
    }

    private static void TryApplyWelcome(Il2Cpp.NewMainMenu menu)
    {
        try
        {
            if (menu == null) return;

            var t = menu.transform.Find(WelcomePath);
            var label = t?.GetComponent<TMP_Text>();
            if (label == null)
            {
                MelonLogger.Warning($"MainMenuCustomizer: Welcome label not found at '{WelcomePath}'.");
                return;
            }

            string username = AuthHandler.PlayerAuth.Username;
            if (string.IsNullOrEmpty(username))
                username = DefaultUsername;

            label.text = Tr.Get(TrKeys.Menu_Welcome, username);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MainMenuCustomizer: welcome failed: {ex.Message}");
        }
    }

    private static void TryApplyVersion(Il2Cpp.NewMainMenu menu)
    {
        try
        {
            if (menu == null) return;

            // NewMainMenu.Version is the inspector-serialized reference to the version
            // label. Direct field access is safer than path lookup because the scene
            // has 6 active Canvas components and FindObjectOfType picks one at random.
            var label = menu.Version;
            if (label == null)
            {
                MelonLogger.Warning("MainMenuCustomizer: NewMainMenu.Version field is null.");
                return;
            }

            string suffix = Tr.Get(TrKeys.Version_ModSuffix);
            string current = label.text ?? string.Empty;
            if (current.EndsWith(suffix, StringComparison.Ordinal)) return;

            label.text = current + suffix;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MainMenuCustomizer: version failed: {ex.Message}");
        }
    }

    private static void TryApplyLogo()
    {
        try
        {
            string logoFilePath = Path.Combine(Paths.SkinsPath, LogoFileName);
            if (!File.Exists(logoFilePath))
            {
                MelonLogger.Msg($"MainMenuCustomizer: no custom logo at '{logoFilePath}', skipping.");
                return;
            }

            var tex = LocalTextures.TryLoad(logoFilePath);
            if (tex == null)
            {
                MelonLogger.Warning($"MainMenuCustomizer: failed to decode '{logoFilePath}'.");
                return;
            }

            // Scene has multiple GameObjects named "Logo" in different sub-trees, but
            // only the active main-menu one (Canvas/Logo, RawImage, stock texture
            // "SLLogoTypeFullWhite") is visible at startup. Targeting active RawImages
            // by name is robust across hierarchy changes between SCPSL versions.
            int swapped = 0;
            foreach (var raw in UnityEngine.Object.FindObjectsOfType<RawImage>())
            {
                if (raw == null || raw.gameObject == null) continue;
                if (!raw.gameObject.activeInHierarchy) continue;
                if (!string.Equals(raw.gameObject.name, "Logo", StringComparison.Ordinal)) continue;
                raw.texture = tex;
                swapped++;
            }

            if (swapped == 0)
                MelonLogger.Warning("MainMenuCustomizer: no active Logo RawImage found to swap.");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MainMenuCustomizer: logo failed: {ex.Message}");
        }
    }

    private static void TryApplyAuthStatus(Il2Cpp.NewMainMenu menu)
    {
        try
        {
            if (menu == null) return;

            string text = IsAuthGood(out var username)
                ? Tr.Get(TrKeys.Menu_AuthConnected, username)
                : Tr.Get(TrKeys.Menu_AuthNotSignedIn);

            AuthStatusOverlay.SetDefault(text);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MainMenuCustomizer: auth status failed: {ex.Message}");
        }
    }

    private static void TryDisableAuthLoading(Il2Cpp.NewMainMenu menu)
    {
        try
        {
            if (menu == null) return;

            var t = menu.transform.Find(AuthLoadingPath);
            if (t == null) return;

            var go = t.gameObject;
            if (go.activeSelf) go.SetActive(false);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MainMenuCustomizer: disable loading failed: {ex.Message}");
        }
    }

    private static bool IsAuthGood(out string username)
    {
        var auth = AuthHandler.PlayerAuth;
        username = auth.Username;
        return !string.IsNullOrEmpty(auth.Username)
            && !string.IsNullOrEmpty(auth.Identity)
            && auth.Identity != "pending";
    }
}
