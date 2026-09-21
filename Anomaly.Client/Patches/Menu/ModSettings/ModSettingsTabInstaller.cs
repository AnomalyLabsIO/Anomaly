using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.UI.ModSettings.Internal;
using Anomaly.Client.Components.ModSettings;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using Il2CppUserSettings.GUIElements;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace Anomaly.Client.Patches.Menu.ModSettings;

/// <summary>
/// Installs a 6th "Mods" tab onto every <see cref="UserSettingsCategories"/>
/// instance the game spawns (NewMainMenu's settings screen AND the in-game
/// pause-menu settings screen). Idempotent across re-entries of the same scene.
/// </summary>
/// <remarks>
/// <para>
/// Run order per scene load:
/// </para>
/// <list type="number">
///   <item>Harmony PREFIX on <c>UserSettingsCategories.Awake</c> calls
///         <see cref="InstallIfNeeded"/>: clones the Activator/Highlight/Group of
///         the "Other" tab and appends a new <c>Category</c> to <c>_categories</c>.</item>
///   <item>Stock Awake iterates the now-6-long array; it wires our new tab's
///         <c>onClick</c> to <c>SelectCategory(5)</c> automatically and runs
///         <c>ToggleCategory(5, false)</c> (group hidden by default).</item>
///   <item>Harmony POSTFIX calls <see cref="PopulateNewlyInstalled"/>: with the
///         group now inactive, the renderer clones widget rows into its Content
///         transform (cloned children's Awake won't fire until the tab is selected).</item>
/// </list>
/// <para>
/// Cloning strategy: every Instantiate happens under a fresh <b>inactive</b>
/// temp parent so the cloned hierarchy's children never fire <c>Awake</c>.
/// Stock generators on the clone (<c>ScpPreferenceListGenerator</c>,
/// <c>UserSettingsUIBase</c>-derived widgets, etc.) are <c>DestroyImmediate</c>d
/// before the clone ever becomes active, so their auto-population logic can't
/// run a second time and clobber the empty Mods tab.
/// </para>
/// </remarks>
internal static class ModSettingsTabInstaller
{
    private static bool _bootstrapped;
    private static readonly HashSet<int> _installedInstances = new();
    private static UserSettingsCategories _pendingPopulate;
    private static RectTransform _pendingContent;
    private static ModSettingsTemplateCache _templates;

    /// <summary>Bootstrap from <c>Core.OnInitializeMelon</c>. Safe to call repeatedly.</summary>
    internal static void Init()
    {
        if (_bootstrapped) return;
        _bootstrapped = true;

        ClientEvents.SceneLoaded += OnSceneLoaded;
        ModSettingsRegistry.OnChanged += OnRegistryChanged;
    }

    /// <summary>Called by the Harmony PREFIX on <c>UserSettingsCategories.Awake</c>.</summary>
    internal static void InstallIfNeeded(UserSettingsCategories controller)
    {
        if (controller == null) return;
        int id = controller.GetInstanceID();
        if (_installedInstances.Contains(id)) return;

        var sceneName = controller.gameObject != null && controller.gameObject.scene.IsValid()
            ? controller.gameObject.scene.name
            : "<unknown>";
        MelonLogger.Msg($"ModSettings: InstallIfNeeded controller={id} scene='{sceneName}' " +
                        $"categories={(controller._categories != null ? controller._categories.Length : -1)}.");

        try
        {
            var template = SelectTemplateCategory(controller);
            if (template == null)
            {
                MelonLogger.Warning("ModSettings: no existing tab to clone from; install aborted.");
                return;
            }

            var activatorClone = CloneActivator(template);
            if (activatorClone == null) return;

            var groupClone = CloneGroup(template, out var contentTransform);
            if (groupClone == null || contentTransform == null) return;

            // Highlight is cloned LAST so we can reparent it onto the cloned Activator
            // — that way it tracks the Mods button's layout-driven position instead of
            // hanging at the original Highlight's anchored position (under Other).
            var highlightClone = CloneHighlight(template, activatorClone);

            var oldArr = controller._categories;
            int oldLen = oldArr != null ? oldArr.Length : 0;
            var newArr = new Il2CppReferenceArray<UserSettingsCategories.Category>(oldLen + 1);
            for (int i = 0; i < oldLen; i++) newArr[i] = oldArr[i];

            var newCategory = new UserSettingsCategories.Category
            {
                Group = groupClone,
                Highlight = highlightClone,
                Activator = activatorClone.GetComponent<Button>(),
            };
            newArr[oldLen] = newCategory;
            controller._categories = newArr;

            _installedInstances.Add(id);
            _pendingPopulate = controller;
            _pendingContent = contentTransform;

            MelonLogger.Msg($"ModSettings: Mods tab installed (instance {id}, categories {oldLen} -> {newArr.Length}).");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"ModSettings: install failed: {ex}");
        }
    }

    /// <summary>Called by the Harmony POSTFIX on <c>UserSettingsCategories.Awake</c>.</summary>
    internal static void PopulateNewlyInstalled(UserSettingsCategories controller)
    {
        if (controller == null || controller != _pendingPopulate) return;
        var content = _pendingContent;
        _pendingPopulate = null;
        _pendingContent = null;

        if (content == null)
        {
            MelonLogger.Warning("ModSettings: PopulateNewlyInstalled called with no pending content.");
            return;
        }

        try
        {
            _templates ??= new ModSettingsTemplateCache();
            _templates.Refresh(controller);
            MelonLogger.Msg($"ModSettings: populating content '{content.name}' (childCount={content.childCount}).");
            ModSettingsRenderer.Attach(content, _templates);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"ModSettings: populate failed: {ex}");
        }
    }

    /// <summary>
    /// Picks a vanilla tab to use as the visual template for the Mods tab.
    /// Walks the <c>_categories</c> array from the end so we land on the last
    /// vanilla tab (typically "Other"), but skips any entry whose Activator is
    /// disabled — that's how the game stores the "Server Specific" tab when no
    /// server settings have arrived yet. Cloning a disabled Activator would
    /// produce a hidden Mods button.
    /// </summary>
    private static UserSettingsCategories.Category SelectTemplateCategory(UserSettingsCategories controller)
    {
        var arr = controller._categories;
        if (arr == null || arr.Length == 0) return null;

        for (int i = arr.Length - 1; i >= 0; i--)
        {
            var cat = arr[i];
            if (cat.Activator == null) continue;
            var actGo = cat.Activator.gameObject;
            if (actGo == null) continue;
            if (!actGo.activeSelf)
            {
                MelonLogger.Msg($"ModSettings: skipping category [{i}] '{actGo.name}' — Activator inactive (likely Server Specific tab).");
                continue;
            }
            MelonLogger.Msg($"ModSettings: using category [{i}] '{actGo.name}' as template.");
            return cat;
        }

        MelonLogger.Warning("ModSettings: no enabled category found; falling back to last entry.");
        return arr[arr.Length - 1];
    }

    /// <summary>
    /// Returns a cloned tab button that's safe to add to <c>_categories</c>:
    /// stock per-tab linker scripts stripped, onClick listeners cleared, label
    /// retargeted to "Mods", positioned at the end of the tab strip.
    /// </summary>
    private static GameObject CloneActivator(UserSettingsCategories.Category template)
    {
        if (template?.Activator == null) return null;
        var src = template.Activator.gameObject;
        var realParent = src.transform.parent;

        var clone = InstantiateInactive(src, realParent, out var holder);
        if (clone == null) return null;
        clone.name = "ModsTab";

        StripStockComponentsRecursive(clone);

        // Move from the inactive holder to the real parent.
        clone.SetActive(true);
        clone.transform.SetParent(realParent, worldPositionStays: false);
        clone.transform.SetAsLastSibling();

        // Stock Awake will add a fresh onClick listener for our new index 5 —
        // any persistent listener the original button shipped with would call
        // SelectCategory on the wrong index.
        var btn = clone.GetComponent<Button>();
        if (btn != null) btn.onClick.RemoveAllListeners();

        var label = clone.GetComponentInChildren<TMP_Text>(includeInactive: true);
        if (label != null) label.text = Tr.Get(TrKeys.Settings_TabMods);

        UnityEngine.Object.Destroy(holder);
        return clone;
    }

    /// <summary>
    /// Clones the active-tab indicator and reparents it onto the cloned Activator
    /// so the underline tracks the Mods button's layout-driven position. Without
    /// reparenting, the clone keeps the source Highlight's anchored position and
    /// renders the underline under "Other" no matter which tab is active.
    /// </summary>
    private static GameObject CloneHighlight(UserSettingsCategories.Category template, GameObject activatorClone)
    {
        if (template?.Highlight == null || activatorClone == null) return null;
        var src = template.Highlight;
        var origActivator = template.Activator?.transform;
        var origHighlight = src.transform;

        var clone = InstantiateInactive(src, src.transform.parent, out var holder);
        if (clone == null) return null;
        clone.name = "ModsHighlight";

        StripStockComponentsRecursive(clone);

        // Capture world-space delta from original Activator to original Highlight
        // BEFORE reparenting — this is how far below (or above) the highlight sits
        // relative to its tab button in the live scene.
        Vector3 worldDelta = Vector3.zero;
        if (origActivator != null)
            worldDelta = origHighlight.position - origActivator.position;

        // Reparent to the cloned Activator. The clone is currently inactive (parent
        // is the inactive holder); SetParent before SetActive(true) keeps Awake
        // suppressed on whatever passive components remain.
        clone.transform.SetParent(activatorClone.transform, worldPositionStays: false);

        // After reparenting, set world position so the highlight sits at the same
        // delta from the cloned Activator as the original was from its activator.
        // (The activator clone's transform.position is the live world position post-
        // layout — at this point in the bootstrap the layout group has not yet
        // re-flowed, so the cloned activator is at the source activator's position,
        // and the worldDelta is correct relative to that. Once the layout group
        // re-flows next frame, the highlight follows the activator automatically
        // because it's a child.)
        clone.transform.position = activatorClone.transform.position + worldDelta;

        clone.SetActive(false);
        UnityEngine.Object.Destroy(holder);
        return clone;
    }

    /// <summary>
    /// Builds the Mods tab's content panel from scratch — explicit
    /// <c>RectTransform</c>, anchors copied from the template tab's group, and a
    /// fresh <c>ScrollRect → Viewport → Content</c> hierarchy. We do not clone
    /// the template group: it carries layout/mask/image components that aren't
    /// safe to inherit and were the cause of the empty-content bug.
    /// </summary>
    private static GameObject CloneGroup(UserSettingsCategories.Category template, out RectTransform contentTransform)
    {
        contentTransform = null;
        if (template?.Group == null) return null;
        var src = template.Group;
        var realParent = src.transform.parent;
        var srcRect = src.GetComponent<RectTransform>();

        // Build the group root with a RectTransform from creation — Il2Cpp Unity's
        // `new GameObject(name, ...types)` requires Il2CppSystem.Type instances.
        GameObject group;
        try
        {
            group = new GameObject(
                "ModsGroup",
                new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"ModSettings: failed to construct ModsGroup with RectTransform: {ex}");
            return null;
        }
        group.SetActive(false);
        group.transform.SetParent(realParent, worldPositionStays: false);

        var rt = group.GetComponent<RectTransform>();
        if (rt != null && srcRect != null)
        {
            rt.anchorMin = srcRect.anchorMin;
            rt.anchorMax = srcRect.anchorMax;
            rt.pivot = srcRect.pivot;
            rt.anchoredPosition = srcRect.anchoredPosition;
            rt.sizeDelta = srcRect.sizeDelta;
            rt.localScale = srcRect.localScale;
            MelonLogger.Msg(
                $"ModSettings: ModsGroup rect anchors=({rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##})-({rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##}) " +
                $"sizeDelta=({rt.sizeDelta.x:0.##},{rt.sizeDelta.y:0.##}).");
        }

        contentTransform = BuildScrollView(group);
        return group;
    }

    /// <summary>
    /// Builds a fresh <c>ScrollView → Viewport → Content</c> hierarchy as a child
    /// of <paramref name="groupRoot"/>. Anchors stretch to fill the group; Content
    /// uses a <see cref="VerticalLayoutGroup"/> + <see cref="ContentSizeFitter"/>
    /// so spawned widget rows lay out top-to-bottom and the scroll height tracks
    /// the spawned children automatically.
    /// </summary>
    private static RectTransform BuildScrollView(GameObject groupRoot)
    {
        // ScrollView root.
        var scrollGo = MakeUiGameObject("ScrollView");
        scrollGo.transform.SetParent(groupRoot.transform, worldPositionStays: false);
        StretchToFillParent(scrollGo.GetComponent<RectTransform>());

        var scrollImage = scrollGo.AddComponent<Image>();
        scrollImage.color = new Color(0.035f, 0.035f, 0.035f, 0.94f);
        scrollImage.raycastTarget = true;

        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        // Viewport (Mask + Image — Mask needs an Image to define the clip area).
        var viewportGo = MakeUiGameObject("Viewport");
        viewportGo.transform.SetParent(scrollGo.transform, worldPositionStays: false);
        StretchToFillParent(viewportGo.GetComponent<RectTransform>());

        var viewportImage = viewportGo.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.004f);
        var mask = viewportGo.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // Content (VerticalLayoutGroup + ContentSizeFitter).
        var contentGo = MakeUiGameObject("Content");
        contentGo.transform.SetParent(viewportGo.transform, worldPositionStays: false);

        var contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);

        var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 12f;
        var padding = new RectOffset();
        padding.left = 16;
        padding.right = 16;
        padding.top = 12;
        padding.bottom = 12;
        vlg.padding = padding;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        // childControlHeight=true tells the VerticalLayoutGroup to resize each
        // row's RectTransform to match its LayoutElement.preferredHeight. Without
        // this, freshly-created row GameObjects keep their default 100x100 rect
        // and render way taller than the 32px we configured.
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = contentGo.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportGo.GetComponent<RectTransform>();
        scroll.content = contentRect;
        contentGo.AddComponent<ModSettingsNavigation>();

        return contentRect;
    }

    /// <summary>
    /// Creates a UI GameObject that has a <see cref="RectTransform"/> from the
    /// instant of construction. In Il2Cpp Unity, <c>new GameObject(name)</c> gives
    /// you a plain <c>Transform</c> and you can't reliably <c>AddComponent</c> a
    /// <c>RectTransform</c> over it; supplying <see cref="Il2CppType.Of"/> to the
    /// types-array constructor is the safe path.
    /// </summary>
    private static GameObject MakeUiGameObject(string name)
        => new GameObject(name, new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });

    private static void StretchToFillParent(RectTransform rt)
    {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Instantiates <paramref name="source"/> under a freshly-created inactive
    /// holder GameObject parented to <paramref name="finalParent"/>. The clone's
    /// children do NOT fire <c>Awake</c> because the holder is inactive in the
    /// hierarchy. The caller is responsible for reparenting the clone out of
    /// the holder and destroying the holder.
    /// </summary>
    private static GameObject InstantiateInactive(GameObject source, Transform finalParent, out GameObject holder)
    {
        holder = new GameObject("ModSettings.InactiveSpawner");
        holder.SetActive(false);
        if (finalParent != null)
            holder.transform.SetParent(finalParent, worldPositionStays: false);

        try
        {
            return UnityEngine.Object.Instantiate(source, holder.transform, worldPositionStays: false);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"ModSettings: InstantiateInactive failed for '{source?.name}': {ex}");
            UnityEngine.Object.Destroy(holder);
            holder = null;
            return null;
        }
    }

    /// <summary>
    /// Destroys every UserSettings / SCP-spawn / server-specific / audio-mixer
    /// generator on the cloned hierarchy. These scripts would otherwise auto-
    /// populate the clone with Other's vanilla widgets on first activation,
    /// producing the duplicate rows the user reports.
    /// </summary>
    private static void StripStockComponentsRecursive(GameObject root)
    {
        if (root == null) return;
        try
        {
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (mb == null) continue;
                var typeName = mb.GetType().Name;
                if (IsStripTarget(typeName))
                {
                    UnityEngine.Object.DestroyImmediate(mb);
                }
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ModSettings: strip stock components failed: {ex.Message}");
        }
    }

    private static bool IsStripTarget(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;

        // Vanilla settings-UI scripts and per-row binders.
        if (typeName.StartsWith("UserSettings", StringComparison.Ordinal)) return true;
        // SCP spawn preference generator + slider rows.
        if (typeName.StartsWith("Scp", StringComparison.Ordinal)) return true;
        // Server-specific settings spawner (rare on Other but defensive).
        if (typeName.StartsWith("SS", StringComparison.Ordinal)) return true;
        // Audio mixer / privacy / keybind / sensitivity / misc-control wiring
        // that lives in UserSettings.AudioSettings, .ControlsSettings, .OtherSettings.
        if (typeName == "MixerAudioSettings" || typeName == "MenuThemeDropdown") return true;
        if (typeName == "OtherAudioSetting" || typeName == "OtherAudioSettings") return true;
        if (typeName == "MiscControlsSetting" || typeName == "SensitivitySetting"
            || typeName == "SensitivitySettings" || typeName == "KeybindEntry"
            || typeName == "KeybindListGenerator") return true;
        if (typeName == "MiscPrivacySetting" || typeName == "MiscPrivacySettings"
            || typeName == "SteamLobbyPrivacy") return true;
        if (typeName == "SettingBasedRolloffCurve" || typeName == "LinkableEnum"
            || typeName == "UserSettingDependency") return true;
        // The categories activator helper (used by the tab button's onClick).
        if (typeName == "UserSettingsCategoryActivator") return true;
        return false;
    }

    private static void OnSceneLoaded(SceneLoadedEventArgs _)
    {
        _installedInstances.Clear();
        _pendingPopulate = null;
        _pendingContent = null;
        _templates = null;
        ModSettingsRenderer.Detach();
    }

    private static void OnRegistryChanged()
    {
        ModSettingsRenderer.RequestRebuild();
    }
}
