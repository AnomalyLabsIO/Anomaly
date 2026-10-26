using System;
using Anomaly.Client.Api.UI;
using Anomaly.Client.Internal.UI;
using Anomaly.Client.Scripting.Output;
using Il2CppTMPro;
using MoonSharp.Interpreter;
using UnityEngine;

namespace Anomaly.Client.Internal.Bootstrap;

/// <summary>Lazy, noninteractive, owner-isolated labels on Anomaly's overlay canvas.</summary>
internal sealed class ScriptTextOverlay : IScriptTextSink
{
    private readonly GameObject[] _objects;
    private readonly TMP_Text[] _labels;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _disposed;

    // No Unity work before the first accepted Set, including on headless hosts.
    internal ScriptTextOverlay()
    {
        _objects = MemoryBudget.Allocate(32 + ScriptOutputHost.MaxDisplayOwners * 8,
            () => new GameObject[ScriptOutputHost.MaxDisplayOwners]);
        _labels = MemoryBudget.Allocate(32 + ScriptOutputHost.MaxDisplayOwners * 8,
            () => new TMP_Text[ScriptOutputHost.MaxDisplayOwners]);
    }

    public bool Set(int slot, string plainText)
    {
        if (_disposed || Environment.CurrentManagedThreadId != _ownerThread || !ValidSlot(slot)) return false;
        var completed = false;
        try
        {
            if (!UiCanvas.IsReady) return false;
            var parent = UiCanvas.GetLayer(UiLayer.Overlay);
            if (parent == null) return false;
            if (_objects[slot] == null || _labels[slot] == null)
            {
                Clear(slot);
                var font = AnomalyUiStyle.ResolveFont();
                if (font == null) return false;
                // Store the root immediately: any later component/property failure
                // is cleaned up by the same owner-slot path as normal disposal.
                var root = new GameObject("Anomaly.ScriptOutput." + slot);
                _objects[slot] = root;
                root.SetActive(false);
                root.transform.SetParent(parent, false);
                var rect = root.AddComponent<RectTransform>();
                var column = slot % 2;
                var row = slot / 2;
                rect.anchorMin = new Vector2(column * 0.5f, 1f - (row + 1) * 0.25f);
                rect.anchorMax = new Vector2((column + 1) * 0.5f, 1f - row * 0.25f);
                // Proportional cells track the actual CanvasScaler dimensions
                // at narrow/ultrawide aspect ratios without creating layout work.
                rect.offsetMin = new Vector2(16f, 12f);
                rect.offsetMax = new Vector2(-16f, -12f);
                var label = root.AddComponent<TextMeshProUGUI>();
                _labels[slot] = label;
                label.font = font;
                label.fontSharedMaterial = font.material;
                label.fontSize = 20f;
                label.enableAutoSizing = false;
                label.enableWordWrapping = true;
                label.richText = false;
                label.parseCtrlCharacters = false;
                label.raycastTarget = false;
                label.alignment = TextAlignmentOptions.TopLeft;
                label.color = Color.white;
                label.overflowMode = TextOverflowModes.Truncate;
                label.maxVisibleCharacters = ScriptOutput.MaxTextBytes;
                label.maxVisibleLines = ScriptOutput.MaxLines;
            }
            _labels[slot].text = plainText;
            _objects[slot].SetActive(true);
            completed = true;
            return true;
        }
        catch (Exception exception) when (ScriptOutputHost.IsRecoverable(exception))
        {
            return false;
        }
        finally
        {
            if (!completed)
            {
                try { Clear(slot); }
                catch (Exception exception) when (ScriptOutputHost.IsRecoverable(exception)) { }
            }
        }
    }

    public void Clear(int slot)
    {
        EnsureOwner();
        if (!ValidSlot(slot)) return;
        var root = _objects[slot];
        _objects[slot] = null;
        _labels[slot] = null;
        if (root == null) return;
        // Disable immediately; Destroy completes at the end of the Unity frame.
        // Always attempt destruction even if a damaged native object cannot hide.
        try { root.SetActive(false); }
        finally { UnityEngine.Object.Destroy(root); }
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        for (var i = 0; i < _objects.Length; i++)
        {
            try { Clear(i); }
            catch (Exception exception) when (ScriptOutputHost.IsRecoverable(exception)) { }
        }
    }

    private static bool ValidSlot(int slot) => slot >= 0 && slot < ScriptOutputHost.MaxDisplayOwners;
    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Script text overlay must run on the Unity thread.");
    }
}
