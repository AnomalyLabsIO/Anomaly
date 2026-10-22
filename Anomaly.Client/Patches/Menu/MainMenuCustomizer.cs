using System;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Assets;
using Anomaly.Client.Auth;
using Anomaly.Client.Components;
using Anomaly.Client.Internal.UI;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using ClientFeature = Anomaly.Client.Api.Features.Client;

namespace Anomaly.Client.Patches.Menu;

/// <summary>
/// Brands the stock <c>NewMainMenu</c> scene: personalized welcome line,
/// TMP version label with a superscript (MODDED) suffix, custom logo,
/// authenticated-user auth status, and the stock Create Server button,
/// reparents the auth-status spinner into the cross-scene connection-info
/// popup, and swaps the server-browser loading panel to host the nicer
/// ConnectPopup body. Subscribes to <see cref="ClientEvents.CanvasReady"/>
/// so it inherits the same timing the rest of the UI bootstrap uses.
/// </summary>
internal static class MainMenuCustomizer
{
    private const string WelcomePath = "News/Welcome";
    private const string VersionLabelName = "Anomaly.Version";
    private const string ServerBrowserLoadingPath = "Servers/ServerBrowser/Loading";
    private const string ConnectPopupRootPath = "ConnectPopup/root";

    private static bool _hooked;
    private static int _connectPopupMenuId;
    private static readonly System.Collections.Generic.List<(RawImage Target, Texture Original)> LogoBindings = new();

    internal static void Init()
    {
        if (_hooked) return;
        _hooked = true;

        ClientEvents.CanvasReady += OnCanvasReady;
        ClientEvents.Connected += OnConnected;
        ClientEvents.Disconnected += OnDisconnected;
    }

    private static void OnConnected(ClientConnectedEventArgs _) => TryApplyAuthStatus(ClientFeature.MainMenu, connected: true);
    private static void OnDisconnected(ClientDisconnectedEventArgs _) => TryApplyAuthStatus(ClientFeature.MainMenu, connected: false);

    internal static void RefreshIdentity()
    {
        var menu = ClientFeature.MainMenu;
        TryApplyWelcome(menu);
        TryApplyAuthStatus(menu);
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
        TryApplyLocalHosting(menu);
        TryRelocateAuthSpinner();
        TryRelocateConnectPopup(menu);
    }

    private static void TryApplyLocalHosting(Il2Cpp.NewMainMenu menu)
    {
        try
        {
            if (menu == null) return;

            // tcs toggles this flag; NewMainMenu.Update uses it to select the
            // stock buttons. The Create Server action reaches the same
            // StartNondedicated confirmation bypass as the host command.
            Il2CppGameCore.Console.DisplayCreateServer = true;
            menu._rentServerButton?.SetActive(false);
            menu._createServerButton?.SetActive(true);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MainMenuCustomizer: local hosting button failed: {ex}");
        }
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
                username = Tr.Get(TrKeys.Menu_DefaultUsername);

            label.text = Tr.Get(TrKeys.Menu_Welcome, username);
            label.richText = false;
            label.color = AnomalyUiStyle.Text;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MainMenuCustomizer: welcome failed: {ex.Message}");
        }
    }

    private static void TryApplyVersion(Il2Cpp.NewMainMenu menu)
    {
        Text source = null;
        TextMeshProUGUI label = null;
        try
        {
            if (menu == null) return;

            // NewMainMenu.Version is the inspector-serialized reference to the version
            // label. Direct field access is safer than path lookup because the scene
            // has 6 active Canvas components and FindObjectOfType picks one at random.
            source = menu.Version;
            if (source == null)
            {
                MelonLogger.Warning("MainMenuCustomizer: NewMainMenu.Version field is null.");
                return;
            }

            string suffix = Tr.Get(TrKeys.Version_ModSuffix);
            string current = source.text ?? string.Empty;
            if (!string.IsNullOrEmpty(suffix) && current.EndsWith(suffix, StringComparison.Ordinal))
                current = current.Substring(0, current.Length - suffix.Length);

            // Keep the game's serialized Text reference valid, with a plain-text
            // fallback. A child TMP graphic replaces its rendering without putting
            // two Graphic components on the same GameObject.
            source.text = current + suffix;
            var existing = source.transform.Find(VersionLabelName);
            label = existing?.GetComponent<TextMeshProUGUI>();
            var template = menu.transform.Find(WelcomePath)?.GetComponent<TMP_Text>();
            var font = template?.font ?? AnomalyUiStyle.ResolveFont();
            if (font == null)
            {
                source.enabled = true;
                if (label != null) label.enabled = false;
                MelonLogger.Warning("MainMenuCustomizer: no TMP font available for the version label.");
                return;
            }

            if (label == null)
            {
                var go = AnomalyUiStyle.MakeUi(VersionLabelName);
                go.transform.SetParent(source.transform, worldPositionStays: false);
                label = go.AddComponent<TextMeshProUGUI>();
            }

            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = source.rectTransform.pivot;

            label.font = font;
            label.fontSharedMaterial = template != null && template.font == font
                ? template.fontSharedMaterial
                : font.material;
            label.fontSize = source.fontSize;
            label.fontStyle = source.fontStyle switch
            {
                FontStyle.Bold => FontStyles.Bold,
                FontStyle.Italic => FontStyles.Italic,
                FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
                _ => FontStyles.Normal,
            };
            label.alignment = VersionAlignment(source.alignment);
            label.color = source.color;
            label.enableAutoSizing = source.resizeTextForBestFit;
            label.fontSizeMin = source.resizeTextMinSize;
            label.fontSizeMax = source.resizeTextMaxSize;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = true;
            label.raycastTarget = false;
            label.maskable = source.maskable;

            // ColorUtility.ToHtmlStringRGB is a throwing unstripped stub in the
            // supported IL2CPP interop assembly. Use the Anomaly accent directly.
            label.text = current + $" <sup><b><color=#D5660D>{suffix.TrimStart()}</color></b></sup>";
            label.enabled = true;
            source.enabled = false;
            MelonLogger.Msg("MainMenuCustomizer: TMP version label applied; legacy Text disabled.");
        }
        catch (Exception ex)
        {
            if (label != null) label.enabled = false;
            if (source != null) source.enabled = true;
            MelonLogger.Warning($"MainMenuCustomizer: version failed: {ex}");
        }
    }

    private static TextAlignmentOptions VersionAlignment(TextAnchor alignment) => alignment switch
    {
        TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
        TextAnchor.UpperCenter => TextAlignmentOptions.Top,
        TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
        TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
        TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
        TextAnchor.MiddleRight => TextAlignmentOptions.Right,
        TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
        TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
        TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
        _ => TextAlignmentOptions.BottomRight,
    };

    private static void TryApplyLogo()
    {
        try
        {
            if (!AssetBundleManager.TryGetLocalTexture("game/skin/logo", out var tex)) return;

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
                if (!LogoBindings.Exists(binding => binding.Target == raw))
                {
                    var original = raw.texture;
                    AssetLifetime.Retain(original);
                    LogoBindings.Add((raw, original));
                }
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

    internal static void RestoreLogo()
    {
        foreach (var binding in LogoBindings)
        {
            if (binding.Target != null) binding.Target.texture = binding.Original;
            AssetLifetime.Release(binding.Original);
        }
        LogoBindings.Clear();
    }

    private static void TryApplyAuthStatus(Il2Cpp.NewMainMenu menu, bool? connected = null)
    {
        try
        {
            if (menu == null) return;

            string text = AuthHandler.InitializationState == AuthInitializationState.Failed
                ? Tr.Get(TrKeys.Menu_AuthUnavailable)
                : IsAuthGood(out var username)
                    ? Tr.Get((connected ?? ClientFeature.IsConnected) ? TrKeys.Menu_AuthConnected : TrKeys.Menu_AuthReady, username)
                    : Tr.Get(TrKeys.Menu_AuthNotSignedIn);

            AuthStatusOverlay.SetDefault(text);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MainMenuCustomizer: auth status failed: {ex.Message}");
        }
    }

    private static void TryRelocateAuthSpinner()
    {
        try
        {
            ConnectionInfoOverlay.RelocateSpinner();
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MainMenuCustomizer: relocate auth spinner failed: {ex.Message}");
        }
    }

    private static void TryRelocateConnectPopup(Il2Cpp.NewMainMenu menu)
    {
        try
        {
            if (menu == null) return;

            // After a game ends and the player returns to menu, a fresh
            // NewMainMenu is instantiated with original ConnectPopup/root
            // children and an enabled LoadingText on ServerBrowser/Loading —
            // we have to redo the relocation per instance. CanvasReady fires
            // once per Start(), so the instance-ID guard only blocks the rare
            // case where Start runs twice on the same GameObject (would
            // otherwise re-disable our reparented children).
            int currentId = menu.GetInstanceID();
            if (_connectPopupMenuId == currentId) return;

            var loading = menu.transform.Find(ServerBrowserLoadingPath);
            if (loading == null)
            {
                MelonLogger.Warning($"MainMenuCustomizer: server browser loading not found at '{ServerBrowserLoadingPath}'.");
                return;
            }

            var root = menu.transform.Find(ConnectPopupRootPath);
            if (root == null)
            {
                MelonLogger.Warning($"MainMenuCustomizer: connect popup root not found at '{ConnectPopupRootPath}'.");
                return;
            }

            // Snapshot child lists before mutating — reparenting changes the live counts.
            int loadingChildCount = loading.childCount;
            var existingChildren = new Transform[loadingChildCount];
            for (int i = 0; i < loadingChildCount; i++)
                existingChildren[i] = loading.GetChild(i);

            int rootChildCount = root.childCount;
            var migrated = new Transform[rootChildCount];
            for (int i = 0; i < rootChildCount; i++)
                migrated[i] = root.GetChild(i);

            // The Loading GameObject itself renders a loading text/graphic
            // (NewServerBrowser.LoadingText is a Text on this object). The
            // GameObject must stay active so reparented ConnectPopup children
            // render — disable the Graphic components on it instead.
            foreach (var graphic in loading.GetComponents<Graphic>())
            {
                if (graphic != null && graphic.enabled)
                    graphic.enabled = false;
            }

            foreach (var child in existingChildren)
            {
                if (child != null && child.gameObject.activeSelf)
                    child.gameObject.SetActive(false);
            }

            foreach (var child in migrated)
            {
                if (child == null) continue;
                child.SetParent(loading, worldPositionStays: false);
            }

            _connectPopupMenuId = currentId;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"MainMenuCustomizer: relocate connect popup failed: {ex.Message}");
        }
    }

    private static bool IsAuthGood(out string username)
    {
        var auth = AuthHandler.PlayerAuth;
        username = auth.Username;
        return AuthHandler.InitializationState == AuthInitializationState.Ready
            && !string.IsNullOrEmpty(auth.Username)
            && !string.IsNullOrEmpty(auth.Identity)
            && auth.Identity != "pending";
    }
}
