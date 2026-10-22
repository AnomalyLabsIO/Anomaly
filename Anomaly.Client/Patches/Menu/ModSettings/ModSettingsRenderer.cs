using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Scheduling;
using Anomaly.Client.Api.UI.ModSettings;
using Anomaly.Client.Api.UI.ModSettings.Internal;
using Anomaly.Client.Components.ModSettings;
using Anomaly.Client.Internal.UI;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Preferences;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Anomaly.Client.Patches.Menu.ModSettings;

/// <summary>
/// Discovers every non-internal <c>MelonPreferences_Category</c> and renders one
/// row per entry into the Mods tab's content panel. All widgets are constructed
/// from primitives (no live scene clones), so vanilla state cannot bleed in.
/// </summary>
internal static class ModSettingsRenderer
{
    private const string AnomalyConfigPrefix = "anomaly_config_";
    private const string AnomalyPersistencePrefix = "anomaly_persistence_";

    // Layout constants. Sized so the bold row label at 22pt fits comfortably with
    // a small amount of vertical breathing room, matching the vanilla tabs.
    private const float WidgetPreferredWidth = 320f;
    private const float WidgetHeight = 48f;
    private const float HeaderHeight = 36f;
    private const float SubHeaderHeight = 24f;
    private const float CategoryGap = 20f;   // blank space before each category header (after the first)
    private const float HeaderFontSize = 26f;
    private const float RowFontSize = 22f;
    private const float WidgetFontSize = 20f;

    // Palette.
    private static readonly Color LabelBg = AnomalyUiStyle.Panel;
    private static readonly Color InputBg = AnomalyUiStyle.Input;
    private static readonly Color SliderTrackBg = new Color(0.05f, 0.05f, 0.05f, 0.85f);
    private static readonly Color SliderFillColor = AnomalyUiStyle.Accent;
    private static readonly Color SliderHandleColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    private static readonly Color DropdownBg = AnomalyUiStyle.Input;
    private static readonly Color HeaderColor = AnomalyUiStyle.Text;
    private static readonly Color TextColor = AnomalyUiStyle.Text;

    private static readonly HashSet<string> _loggedUnsupportedTypes = new HashSet<string>(StringComparer.Ordinal);

    private static RectTransform _content;
    private static ModSettingsTemplateCache _templates;
    private static ScheduledTask _pendingRebuild;

    internal static void Attach(RectTransform content, ModSettingsTemplateCache templates)
    {
        _content = content;
        _templates = templates;
        PopulateInto(content);
    }

    internal static void Detach()
    {
        _content = null;
        _templates = null;
        _pendingRebuild?.Cancel();
        _pendingRebuild = null;
    }

    internal static void RequestRebuild()
    {
        if (_content == null || _templates == null) return;
        if (_pendingRebuild != null) return;
        _pendingRebuild = Scheduler.In(TimeSpan.Zero, () =>
        {
            _pendingRebuild = null;
            if (_content == null) return;
            PopulateInto(_content);
        }, ownerId: "anomaly.modsettings.rebuild");
    }

    private static void PopulateInto(RectTransform content)
    {
        if (content == null)
        {
            MelonLogger.Warning("ModSettings: PopulateInto called with null content.");
            return;
        }

        var selected = EventSystem.current?.currentSelectedGameObject;
        string selectedRow = selected?.GetComponentInParent<ModSettingsRowState>()?.gameObject.name;
        string selectedControl = selected?.name;
        Vector2 scrollPosition = content.anchoredPosition;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            try { UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject); }
            catch (Exception ex) { MelonLogger.Warning($"ModSettings: clear child failed: {ex.Message}"); }
        }

        if (_templates == null || !_templates.HasFont)
        {
            MelonLogger.Msg("ModSettings: font not yet cached; rendering empty-state.");
            SpawnLabelOnly(content, Tr.Get(TrKeys.Settings_ModsEmptyState), large: true);
            return;
        }

        var categories = EnumerateRenderableCategories().ToList();
        MelonLogger.Msg($"ModSettings: rendering {categories.Count} categor{(categories.Count == 1 ? "y" : "ies")}.");
        if (categories.Count == 0)
        {
            SpawnLabelOnly(content, Tr.Get(TrKeys.Settings_ModsEmptyState), large: true);
            return;
        }

        bool first = true;
        foreach (var (category, categoryMeta) in categories)
        {
            // Visual gap before every category except the very first.
            if (!first) SpawnSpacer(content, CategoryGap);
            first = false;

            string categoryLabel = categoryMeta?.DisplayName ?? category.DisplayName ?? category.Identifier;
            SpawnLabelOnly(content, categoryLabel, large: true);

            var entries = EnumerateRenderableEntries(category).ToList();
            MelonLogger.Msg($"ModSettings:   category '{categoryLabel}' -> {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")}.");

            string previousGroup = null;
            foreach (var (entry, entryMeta) in entries)
            {
                if (entryMeta?.Group is { } group && !string.Equals(group, previousGroup, StringComparison.Ordinal))
                {
                    SpawnLabelOnly(content, group, large: false);
                }
                previousGroup = entryMeta?.Group;
                SpawnRow(content, entry, entryMeta);
            }
        }

        Canvas.ForceUpdateCanvases();
        content.anchoredPosition = scrollPosition;
        if (selectedRow != null && EventSystem.current != null)
        {
            foreach (var control in content.GetComponentsInChildren<Selectable>(includeInactive: true))
            {
                if (control.name == selectedControl
                    && control.GetComponentInParent<ModSettingsRowState>()?.gameObject.name == selectedRow
                    && control.IsInteractable() && control.gameObject.activeInHierarchy)
                {
                    EventSystem.current.SetSelectedGameObject(control.gameObject);
                    break;
                }
            }
        }
    }

    // ---- Category / entry filtering ----

    private static IEnumerable<(MelonPreferences_Category Cat, ModSettingCategoryMetadata Meta)> EnumerateRenderableCategories()
    {
        var list = MelonPreferences.Categories;
        if (list == null) yield break;

        var staged = new List<(MelonPreferences_Category, ModSettingCategoryMetadata)>();
        foreach (var cat in list)
        {
            if (cat == null) continue;
            string id = cat.Identifier ?? string.Empty;
            if (id.StartsWith(AnomalyConfigPrefix, StringComparison.Ordinal)) continue;
            if (id.StartsWith(AnomalyPersistencePrefix, StringComparison.Ordinal)) continue;
            if (cat.IsHidden) continue;

            var meta = ModSettingsRegistry.GetCategory(id);
            if (meta?.Hidden == true) continue;
            if (!EnumerateRenderableEntries(cat).Any()) continue;

            staged.Add((cat, meta));
        }

        foreach (var item in staged
                     .OrderBy(x => x.Item2?.SortOrder ?? 0)
                     .ThenBy(x => x.Item2?.DisplayName ?? x.Item1.DisplayName ?? x.Item1.Identifier, StringComparer.Ordinal))
        {
            yield return item;
        }
    }

    private static IEnumerable<(MelonPreferences_Entry Entry, ModSettingMetadata Meta)> EnumerateRenderableEntries(MelonPreferences_Category category)
    {
        if (category?.Entries == null) yield break;

        var staged = new List<(MelonPreferences_Entry, ModSettingMetadata)>();
        foreach (var entry in category.Entries)
        {
            if (entry == null) continue;
            if (entry.IsHidden) continue;

            var meta = ModSettingsRegistry.GetEntry(category.Identifier, entry.Identifier);
            if (meta?.Hidden == true) continue;
            if (!IsRenderableType(entry, meta)) continue;

            staged.Add((entry, meta));
        }

        foreach (var item in staged
                     .OrderBy(x => x.Item2?.Order ?? 0)
                     .ThenBy(x => x.Item2?.DisplayName ?? x.Item1.DisplayName ?? x.Item1.Identifier, StringComparer.Ordinal))
        {
            yield return item;
        }
    }

    private static bool IsRenderableType(MelonPreferences_Entry entry, ModSettingMetadata meta)
    {
        if (meta?.Choices is { Count: > 0 }) return true;

        Type t;
        try { t = entry.GetReflectedType(); }
        catch { return false; }
        if (t == null) return false;
        if (t == typeof(bool)) return true;
        if (t == typeof(string)) return true;
        if (t.IsEnum) return true;
        if (IsNumeric(t)) return true;

        string typeName = t.Name;
        if (_loggedUnsupportedTypes.Add(typeName))
            MelonLogger.Msg($"ModSettings: skipping unsupported entry type '{typeName}'.");
        return false;
    }

    private static bool IsNumeric(Type t)
        => ModSettingValue.IsNumeric(t);

    // ---- Row dispatcher ----

    private static void SpawnRow(RectTransform content, MelonPreferences_Entry entry, ModSettingMetadata meta)
    {
        var row = BuildRow(content, entry, meta, out var controls, out var value, out var feedback);
        if (row == null) return;
        var rowState = row.AddComponent<ModSettingsRowState>();
        BuildWidget(controls, entry, meta);
        var reset = BuildSubButton(controls, "Reset", Tr.Get(TrKeys.Settings_Reset));
        var resetLayout = reset.go.GetComponent<LayoutElement>();
        resetLayout.minWidth = 112f;
        resetLayout.preferredWidth = 112f;
        resetLayout.flexibleWidth = 0f;
        var retry = BuildSubButton(row.transform, "RetrySave", Tr.Get(TrKeys.Settings_RetrySave));
        rowState.Initialize(entry, value, feedback, reset.btn, retry.btn);
        row.SetActive(true);
    }

    private static void BuildWidget(Transform parent, MelonPreferences_Entry entry, ModSettingMetadata meta)
    {
        // meta.Choices forces a dropdown regardless of type.
        if (meta?.Choices is { Count: > 0 })
        {
            BuildDropdownWidget(parent, entry, meta, meta.Choices, isEnum: entry.GetReflectedType().IsEnum);
            MelonLogger.Msg($"ModSettings:     spawned dropdown (choices) row for '{entry.Identifier}'.");
            return;
        }

        Type t = entry.GetReflectedType();

        if (t == typeof(bool))
        {
            BuildTwoButtonsWidget(parent, entry);
            MelonLogger.Msg($"ModSettings:     spawned bool row for '{entry.Identifier}'.");
            return;
        }

        if (t.IsEnum)
        {
            BuildDropdownWidget(parent, entry, meta, Enum.GetNames(t), isEnum: true);
            MelonLogger.Msg($"ModSettings:     spawned enum row for '{entry.Identifier}'.");
            return;
        }

        if (IsNumeric(t))
        {
            // Preserve exact long/decimal values with text input rather than a float slider.
            if (t != typeof(long) && t != typeof(decimal)
                && TryGetRange(entry, meta, out float min, out float max))
            {
                BuildSliderWidget(parent, entry, meta, min, max,
                    wholeNumbers: t == typeof(int) || t == typeof(long) || t == typeof(short));
                MelonLogger.Msg($"ModSettings:     spawned slider row for '{entry.Identifier}' ({min}..{max}).");
                return;
            }
            BuildInputField(parent, entry, meta);
            MelonLogger.Msg($"ModSettings:     spawned numeric input row for '{entry.Identifier}'.");
            return;
        }

        // string (and anything else falling through)
        BuildInputField(parent, entry, meta);
        MelonLogger.Msg($"ModSettings:     spawned input row for '{entry.Identifier}'.");
    }

    private static bool TryGetRange(MelonPreferences_Entry entry, ModSettingMetadata meta, out float min, out float max)
    {
        min = 0f; max = 0f;
        if (meta?.MinValue is { } mn && meta.MaxValue is { } mx && mx > mn)
        {
            min = (float)mn; max = (float)mx;
            return float.IsFinite(min) && float.IsFinite(max) && max > min;
        }
        if (entry.Validator is IValueRange range)
        {
            try
            {
                min = Convert.ToSingle(range.MinValue);
                max = Convert.ToSingle(range.MaxValue);
                return float.IsFinite(min) && float.IsFinite(max) && max > min;
            }
            catch { return false; }
        }
        return false;
    }

    private static string ResolveLabel(MelonPreferences_Entry entry, ModSettingMetadata meta)
        => meta?.DisplayName ?? entry.DisplayName ?? entry.Identifier;

    // ---- Layout primitives ----

    /// <summary>Make a UI GameObject that has a <see cref="RectTransform"/> from creation.</summary>
    private static GameObject MakeUi(string name)
        => AnomalyUiStyle.MakeUi(name);

    private static GameObject BuildRow(RectTransform content, MelonPreferences_Entry entry, ModSettingMetadata meta,
        out Transform controls, out TMP_Text value, out TMP_Text feedback)
    {
        controls = null;
        value = null;
        feedback = null;
        GameObject row;
        try { row = MakeUi("ModSettings.Row." + entry.Category.Identifier + "." + entry.Identifier); }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings: row construction failed: {ex.Message}"); return null; }
        // Keep binding deterministic even when rebuilding a visible tab.
        row.SetActive(false);
        row.transform.SetParent(content, worldPositionStays: false);
        var background = row.AddComponent<Image>();
        background.color = LabelBg;
        background.raycastTarget = false;
        ApplyRoundedBg(background);
        var layout = row.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset { left = 16, right = 16, top = 12, bottom = 12 };
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        BuildFlowText(row.transform, "Label", ResolveLabel(entry, meta), RowFontSize, TextColor);
        string description = meta?.Description ?? entry.Description;
        if (!string.IsNullOrWhiteSpace(description))
            BuildFlowText(row.transform, "Description", description, 18f, AnomalyUiStyle.Secondary);
        var controlsGo = MakeUi("Controls");
        controlsGo.transform.SetParent(row.transform, false);
        var hlg = controlsGo.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 12f;
        controlsGo.AddComponent<LayoutElement>().minHeight = WidgetHeight;
        controls = controlsGo.transform;
        value = BuildFlowText(row.transform, "Value", "", 18f, AnomalyUiStyle.Secondary);
        feedback = BuildFlowText(row.transform, "Feedback", "", 18f, AnomalyUiStyle.Error);
        return row;
    }

    private static TMP_Text BuildFlowText(Transform parent, string name, string text, float size, Color color)
    {
        var textGo = MakeUi(name);
        textGo.transform.SetParent(parent, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(tmp);
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.enableWordWrapping = true;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void SpawnLabelOnly(RectTransform parent, string text, bool large)
    {
        try
        {
            var go = MakeUi(large ? "ModSettings.Header" : "ModSettings.SubHeader");
            go.transform.SetParent(parent, worldPositionStays: false);

            go.AddComponent<LayoutElement>().minHeight = large ? HeaderHeight : SubHeaderHeight;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            ApplyFont(tmp);
            // Match vanilla: category headers are uppercase and centered (see the
            // "PRIVACY" header in the Other tab). Sub-headers stay left-aligned.
            tmp.text = text ?? string.Empty;
            tmp.fontSize = large ? HeaderFontSize : RowFontSize;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = true;
            tmp.color = large ? HeaderColor : new Color(0.85f, 0.85f, 0.85f, 1f);
            tmp.raycastTarget = false;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ModSettings: spawn header failed: {ex.Message}");
        }
    }

    /// <summary>Insert a blank vertical spacer of <paramref name="height"/> pixels.</summary>
    private static void SpawnSpacer(RectTransform parent, float height)
    {
        try
        {
            var go = MakeUi("ModSettings.Spacer");
            go.transform.SetParent(parent, worldPositionStays: false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleWidth = 1f;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ModSettings: spawn spacer failed: {ex.Message}");
        }
    }

    private static void ApplyFont(TMP_Text tmp)
    {
        AnomalyUiStyle.ApplyText(tmp, WidgetFontSize);
        if (_templates?.Font != null)
        {
            tmp.font = _templates.Font;
            if (_templates.FontMaterial != null) tmp.fontSharedMaterial = _templates.FontMaterial;
        }
        tmp.fontStyle = FontStyles.Normal;

        // Auto-sizing intentionally disabled — vanilla handles scaling through
        // some other path we haven't identified yet. fontSize from the call site
        // is used directly; min/max remain in case we re-enable auto-sizing later.
        tmp.enableAutoSizing = false;
        tmp.fontSizeMin = 18f;
        tmp.fontSizeMax = 72f;
    }

    /// <summary>
    /// Apply our rounded-rect sprite to a background <see cref="Image"/>. Image
    /// type becomes <see cref="Image.Type.Sliced"/> so the corners stay rounded
    /// when the rect resizes. The image's color (set by the caller) tints the
    /// white sprite.
    /// </summary>
    private static void ApplyRoundedBg(Image image)
    {
        if (image == null) return;
        if (_templates?.RoundedRectSprite == null) return;
        image.sprite = _templates.RoundedRectSprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
    }

    // ---- Widget builders (all from scratch) ----

    /// <summary>Two side-by-side buttons (FALSE / TRUE) — active side colored orange.</summary>
    private static void BuildTwoButtonsWidget(Transform parent, MelonPreferences_Entry entry)
    {
        var container = MakeUi("TwoButtons");
        container.transform.SetParent(parent, worldPositionStays: false);

        var le = container.AddComponent<LayoutElement>();
        le.preferredWidth = WidgetPreferredWidth;
        le.flexibleWidth = 1f;
        le.preferredHeight = WidgetHeight;

        var hlg = container.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 2f;

        var (falseGo, falseImg, falseBtn) = BuildSubButton(container.transform, "FalseBtn", Tr.Get(TrKeys.Settings_Disabled));
        var (trueGo, trueImg, trueBtn) = BuildSubButton(container.transform, "TrueBtn", Tr.Get(TrKeys.Settings_Enabled));

        var binder = container.AddComponent<ModSettingsToggleBinder>();
        binder.Initialize(entry, trueImage: trueImg, falseImage: falseImg);

        // Wire each button's onClick to the binder's SetValue.
        var trueListener = DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => binder.SetValue(true)));
        var falseListener = DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => binder.SetValue(false)));
        trueBtn.onClick.AddListener(trueListener);
        falseBtn.onClick.AddListener(falseListener);
    }

    private static (GameObject go, Image bg, Button btn) BuildSubButton(Transform parent, string name, string label)
    {
        var go = MakeUi(name);
        go.transform.SetParent(parent, worldPositionStays: false);

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = WidgetHeight;
        le.flexibleWidth = 1f;

        var bg = go.AddComponent<Image>();
        bg.color = ModSettingsToggleBinder.InactiveColor;
        bg.raycastTarget = true;
        ApplyRoundedBg(bg);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        AnomalyUiStyle.ApplySelectable(btn);

        var textGo = MakeUi("Text");
        textGo.transform.SetParent(go.transform, worldPositionStays: false);
        var rt = textGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(tmp);
        tmp.text = label;
        tmp.fontSize = WidgetFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.color = TextColor;
        tmp.raycastTarget = false;

        return (go, bg, btn);
    }

    private static void BuildSliderWidget(Transform parent, MelonPreferences_Entry entry, ModSettingMetadata meta, float min, float max, bool wholeNumbers)
    {
        var container = MakeUi("Slider");
        container.transform.SetParent(parent, worldPositionStays: false);

        var le = container.AddComponent<LayoutElement>();
        le.preferredWidth = WidgetPreferredWidth;
        le.flexibleWidth = 1f;
        le.preferredHeight = WidgetHeight;

        // Background track.
        var bg = container.AddComponent<Image>();
        bg.color = SliderTrackBg;
        bg.raycastTarget = true;
        ApplyRoundedBg(bg);

        var slider = container.AddComponent<Slider>();

        // Fill area.
        var fillArea = MakeUi("Fill Area");
        fillArea.transform.SetParent(container.transform, worldPositionStays: false);
        var faRect = fillArea.GetComponent<RectTransform>();
        faRect.anchorMin = new Vector2(0f, 0.25f);
        faRect.anchorMax = new Vector2(1f, 0.75f);
        faRect.offsetMin = new Vector2(8f, 0f);
        faRect.offsetMax = new Vector2(-8f, 0f);

        var fillGo = MakeUi("Fill");
        fillGo.transform.SetParent(fillArea.transform, worldPositionStays: false);
        var fillRect = fillGo.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        var fillImage = fillGo.AddComponent<Image>();
        fillImage.color = SliderFillColor;
        fillImage.raycastTarget = false;
        ApplyRoundedBg(fillImage);

        // Handle area.
        var handleArea = MakeUi("Handle Slide Area");
        handleArea.transform.SetParent(container.transform, worldPositionStays: false);
        var haRect = handleArea.GetComponent<RectTransform>();
        haRect.anchorMin = Vector2.zero;
        haRect.anchorMax = Vector2.one;
        haRect.offsetMin = new Vector2(8f, 0f);
        haRect.offsetMax = new Vector2(-8f, 0f);

        var handleGo = MakeUi("Handle");
        handleGo.transform.SetParent(handleArea.transform, worldPositionStays: false);
        var handleRect = handleGo.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0f, 0f);
        handleRect.anchorMax = new Vector2(0f, 1f);
        handleRect.sizeDelta = new Vector2(16f, 0f);
        var handleImage = handleGo.AddComponent<Image>();
        handleImage.color = SliderHandleColor;
        handleImage.raycastTarget = true;
        ApplyRoundedBg(handleImage);

        slider.targetGraphic = handleImage;
        AnomalyUiStyle.ApplySelectable(slider);
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = wholeNumbers;

        var binder = container.AddComponent<ModSettingsSliderBinder>();
        binder.Initialize(entry, meta, min, max, wholeNumbers);
    }

    private static void BuildDropdownWidget(Transform parent, MelonPreferences_Entry entry, ModSettingMetadata meta, IReadOnlyList<string> options, bool isEnum)
    {
        var container = MakeUi("Dropdown");
        container.transform.SetParent(parent, worldPositionStays: false);

        var le = container.AddComponent<LayoutElement>();
        le.preferredWidth = WidgetPreferredWidth;
        le.flexibleWidth = 1f;
        le.preferredHeight = WidgetHeight;

        var bg = container.AddComponent<Image>();
        bg.color = DropdownBg;
        bg.raycastTarget = true;
        ApplyRoundedBg(bg);

        var dropdown = container.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = bg;
        AnomalyUiStyle.ApplySelectable(dropdown);

        // Caption text (visible current selection).
        var captionGo = MakeUi("Label");
        captionGo.transform.SetParent(container.transform, worldPositionStays: false);
        var capRect = captionGo.GetComponent<RectTransform>();
        capRect.anchorMin = Vector2.zero;
        capRect.anchorMax = Vector2.one;
        capRect.offsetMin = new Vector2(12f, 0f);
        capRect.offsetMax = new Vector2(-32f, 0f);
        var caption = captionGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(caption);
        caption.fontSize = WidgetFontSize;
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.enableWordWrapping = true;
        caption.color = TextColor;
        caption.raycastTarget = false;
        dropdown.captionText = caption;

        // Arrow (decorative).
        var arrowGo = MakeUi("Arrow");
        arrowGo.transform.SetParent(container.transform, worldPositionStays: false);
        var arRect = arrowGo.GetComponent<RectTransform>();
        arRect.anchorMin = new Vector2(1f, 0.5f);
        arRect.anchorMax = new Vector2(1f, 0.5f);
        arRect.pivot = new Vector2(1f, 0.5f);
        arRect.anchoredPosition = new Vector2(-8f, 0f);
        arRect.sizeDelta = new Vector2(16f, 16f);
        var arrowImg = arrowGo.AddComponent<Image>();
        arrowImg.color = new Color(1f, 1f, 1f, 0.6f);
        arrowImg.raycastTarget = false;

        // Template (popup) — required by TMP_Dropdown to display options.
        var template = BuildDropdownTemplate(container.transform);
        dropdown.template = template.GetComponent<RectTransform>();

        var itemLabel = template.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
        if (itemLabel != null) dropdown.itemText = itemLabel;

        var binder = container.AddComponent<ModSettingsDropdownBinder>();
        binder.Initialize(entry, meta, options, isEnum);
    }

    private static GameObject BuildDropdownTemplate(Transform parent)
    {
        // Top-level template, initially inactive.
        var template = MakeUi("Template");
        template.transform.SetParent(parent, worldPositionStays: false);
        var tRect = template.GetComponent<RectTransform>();
        tRect.anchorMin = new Vector2(0f, 0f);
        tRect.anchorMax = new Vector2(1f, 0f);
        tRect.pivot = new Vector2(0.5f, 1f);
        tRect.anchoredPosition = new Vector2(0f, 0f);
        tRect.sizeDelta = new Vector2(0f, 240f);
        var templateBg = template.AddComponent<Image>();
        templateBg.color = DropdownBg;
        ApplyRoundedBg(templateBg);
        template.SetActive(false);

        // Viewport with RectMask2D so items are clipped.
        var viewport = MakeUi("Viewport");
        viewport.transform.SetParent(template.transform, worldPositionStays: false);
        var vRect = viewport.GetComponent<RectTransform>();
        vRect.anchorMin = Vector2.zero;
        vRect.anchorMax = Vector2.one;
        vRect.offsetMin = Vector2.zero;
        vRect.offsetMax = Vector2.zero;
        viewport.AddComponent<RectMask2D>();

        // Content holder.
        var contentGo = MakeUi("Content");
        contentGo.transform.SetParent(viewport.transform, worldPositionStays: false);
        var cRect = contentGo.GetComponent<RectTransform>();
        cRect.anchorMin = new Vector2(0f, 1f);
        cRect.anchorMax = new Vector2(1f, 1f);
        cRect.pivot = new Vector2(0.5f, 1f);
        cRect.anchoredPosition = Vector2.zero;
        cRect.sizeDelta = new Vector2(0f, WidgetHeight);
        var scroll = template.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.viewport = vRect;
        scroll.content = cRect;

        // Item — TMP_Dropdown instantiates one per option using this as a template.
        var item = MakeUi("Item");
        item.transform.SetParent(contentGo.transform, worldPositionStays: false);
        var iRect = item.GetComponent<RectTransform>();
        iRect.anchorMin = new Vector2(0f, 0.5f);
        iRect.anchorMax = new Vector2(1f, 0.5f);
        iRect.sizeDelta = new Vector2(0f, WidgetHeight);
        var itemBg = item.AddComponent<Image>();
        itemBg.color = DropdownBg;
        var itemToggle = item.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemBg;
        AnomalyUiStyle.ApplySelectable(itemToggle);

        var itemBgChild = MakeUi("Item Background");
        itemBgChild.transform.SetParent(item.transform, worldPositionStays: false);
        var ibRect = itemBgChild.GetComponent<RectTransform>();
        ibRect.anchorMin = Vector2.zero;
        ibRect.anchorMax = Vector2.one;
        ibRect.offsetMin = Vector2.zero;
        ibRect.offsetMax = Vector2.zero;
        itemBgChild.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        var itemLabelGo = MakeUi("Item Label");
        itemLabelGo.transform.SetParent(item.transform, worldPositionStays: false);
        var ilRect = itemLabelGo.GetComponent<RectTransform>();
        ilRect.anchorMin = Vector2.zero;
        ilRect.anchorMax = Vector2.one;
        ilRect.offsetMin = new Vector2(12f, 0f);
        ilRect.offsetMax = new Vector2(-12f, 0f);
        var itemTmp = itemLabelGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(itemTmp);
        itemTmp.fontSize = WidgetFontSize;
        itemTmp.alignment = TextAlignmentOptions.MidlineLeft;
        itemTmp.enableWordWrapping = true;
        itemTmp.color = TextColor;
        itemTmp.raycastTarget = false;

        return template;
    }

    private static void BuildInputField(Transform parent, MelonPreferences_Entry entry, ModSettingMetadata meta)
    {
        GameObject inputGo;
        try { inputGo = MakeUi("Input"); }
        catch (Exception ex) { MelonLogger.Warning($"ModSettings: input construction failed: {ex.Message}"); return; }
        inputGo.transform.SetParent(parent, worldPositionStays: false);

        var le = inputGo.AddComponent<LayoutElement>();
        le.preferredWidth = WidgetPreferredWidth;
        le.flexibleWidth = 1f;
        le.preferredHeight = WidgetHeight;

        var bg = inputGo.AddComponent<Image>();
        bg.color = InputBg;
        bg.raycastTarget = true;
        ApplyRoundedBg(bg);

        var input = inputGo.AddComponent<TMP_InputField>();

        var taGo = MakeUi("TextArea");
        taGo.transform.SetParent(inputGo.transform, worldPositionStays: false);
        var taRect = taGo.GetComponent<RectTransform>();
        taRect.anchorMin = Vector2.zero;
        taRect.anchorMax = Vector2.one;
        taRect.offsetMin = new Vector2(8f, 4f);
        taRect.offsetMax = new Vector2(-8f, -4f);
        taGo.AddComponent<RectMask2D>();

        var textGo = MakeUi("Text");
        textGo.transform.SetParent(taGo.transform, worldPositionStays: false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var textTmp = textGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(textTmp);
        textTmp.text = string.Empty;
        textTmp.fontSize = WidgetFontSize;
        textTmp.alignment = TextAlignmentOptions.MidlineLeft;
        textTmp.enableWordWrapping = false;
        textTmp.color = TextColor;
        textTmp.raycastTarget = false;

        input.textViewport = taRect;
        input.textComponent = textTmp;
        input.targetGraphic = bg;
        input.lineType = TMP_InputField.LineType.SingleLine;
        AnomalyUiStyle.ApplySelectable(input);

        var binder = inputGo.AddComponent<ModSettingsInputFieldBinder>();
        binder.Initialize(entry, meta);
    }
}
