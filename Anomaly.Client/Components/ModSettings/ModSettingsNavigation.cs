using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Anomaly.Client.Components.ModSettings;

/// <summary>Tab traversal and keeping the selected control visible in the Mods list.</summary>
[RegisterTypeInIl2Cpp]
public sealed class ModSettingsNavigation : MonoBehaviour
{
    public ModSettingsNavigation(IntPtr pointer) : base(pointer) { }
    public ModSettingsNavigation() { }

    private GameObject _lastSelected;

    private void Update()
    {
        var events = EventSystem.current;
        if (events == null) return;
        var selected = events.currentSelectedGameObject;
        bool inside = selected != null && selected.transform.IsChildOf(transform);
        if (Input.GetKeyDown(KeyCode.Tab)
            && (inside || selected == null || selected.name == "ModsTab"))
        {
            var controls = new List<Selectable>();
            foreach (var control in GetComponentsInChildren<Selectable>())
                if (control.IsActive() && control.IsInteractable()) controls.Add(control);
            if (controls.Count > 0)
            {
                int index = controls.FindIndex(control => control.gameObject == selected);
                bool backwards = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                index = index < 0 ? (backwards ? controls.Count - 1 : 0)
                    : (index + (backwards ? -1 : 1) + controls.Count) % controls.Count;
                selected = controls[index].gameObject;
                events.SetSelectedGameObject(selected);
                inside = true;
            }
        }
        if (!inside || selected == _lastSelected) return;
        _lastSelected = selected;
        var scroll = GetComponentInParent<ScrollRect>();
        var target = selected.GetComponent<RectTransform>();
        if (scroll == null || scroll.viewport == null || target == null) return;
        Canvas.ForceUpdateCanvases();
        // CalculateRelativeRectTransformBounds is an unsupported unstripped helper
        // in the client interop assembly. Transform the control's four corners instead.
        Rect targetRect = target.rect;
        float left = targetRect.xMin;
        float right = targetRect.xMax;
        float bottom = targetRect.yMin;
        float top = targetRect.yMax;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        for (int corner = 0; corner < 4; corner++)
        {
            var local = new Vector3((corner & 1) == 0 ? left : right,
                (corner & 2) == 0 ? bottom : top, 0f);
            float y = scroll.viewport.InverseTransformPoint(target.TransformPoint(local)).y;
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
        Rect viewport = scroll.viewport.rect;
        float delta = minY < viewport.yMin ? viewport.yMin - minY
            : maxY > viewport.yMax ? viewport.yMax - maxY : 0f;
        if (Mathf.Abs(delta) > 0.01f)
            scroll.content.anchoredPosition += new Vector2(0f, delta);
    }
}
