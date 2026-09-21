using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Internal;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Api.Input;

/// <summary>
/// Lets mods inject input into the game's <see cref="UnityEngine.Input"/>
/// pipeline without each mod re-patching <c>Input.GetKey</c> /
/// <c>Input.GetAxis</c> by hand. Useful for controller support, accessibility
/// modes, gesture systems, and custom input devices.
/// </summary>
/// <remarks>
/// <para>Three provider types — <c>Button</c> (one per <see cref="KeyCode"/>),
/// <c>MouseButton</c> (one per mouse-button index), and <c>Axis</c> (one per
/// axis-name string like <c>"Horizontal"</c>). Multiple mods can register
/// providers for the same key/axis; results are merged additively (axes are
/// summed and clamped to <c>[-1, 1]</c>; buttons are OR'd).</para>
/// <para>Down/Up transitions for buttons are computed once per frame by an
/// internal poller — providers just return current state; the poller infers
/// "newly pressed this frame" / "newly released this frame".</para>
/// <para>Every <c>Register*</c> call returns an <see cref="IDisposable"/>
/// handle. Disposing the handle removes the provider; mods that just hold
/// providers for the session don't need to dispose. <see cref="UnregisterAllForMod"/>
/// is the bulk path for cleanup.</para>
/// </remarks>
public static class VirtualInputs
{
    private sealed class ButtonProvider
    {
        public string Id;
        public KeyCode Key;
        public Func<bool> Provider;
        public bool LastFrameValue;
        public bool DownThisFrame;
        public bool UpThisFrame;
    }

    private sealed class MouseButtonProvider
    {
        public string Id;
        public int Button;
        public Func<bool> Provider;
        public bool LastFrameValue;
        public bool DownThisFrame;
        public bool UpThisFrame;
    }

    private sealed class AxisProvider
    {
        public string Id;
        public string AxisName;
        public Func<float> Provider;
    }

    private static readonly Dictionary<string, ButtonProvider> _buttons = new();
    private static readonly Dictionary<string, MouseButtonProvider> _mouseButtons = new();
    private static readonly Dictionary<string, AxisProvider> _axes = new();

    /// <summary>
    /// Registers a virtual button that OR's with <see cref="UnityEngine.Input.GetKey(KeyCode)"/>
    /// for <paramref name="key"/>. Down/Up transitions are computed automatically.
    /// </summary>
    /// <param name="id">Unique mod-namespaced id (e.g. <c>"mymod.fire"</c>).</param>
    /// <param name="key">Which <see cref="KeyCode"/> to inject into.</param>
    /// <param name="provider">Returns the current pressed state every frame.</param>
    /// <returns>Handle that removes the provider on dispose.</returns>
    public static IDisposable RegisterButton(string id, KeyCode key, Func<bool> provider)
    {
        ValidateId(id);
        if (provider == null) throw new ArgumentNullException(nameof(provider));

        if (_buttons.ContainsKey(id))
        {
            MelonLogger.Warning($"VirtualInputs: button '{id}' already registered; ignoring duplicate.");
            return Noop.Instance;
        }

        _buttons[id] = new ButtonProvider { Id = id, Key = key, Provider = provider };
        return new ButtonHandle(id);
    }

    /// <summary>
    /// Registers a virtual mouse button that OR's with
    /// <see cref="UnityEngine.Input.GetMouseButton(int)"/> for
    /// <paramref name="button"/> (0 = left, 1 = right, 2 = middle).
    /// </summary>
    public static IDisposable RegisterMouseButton(string id, int button, Func<bool> provider)
    {
        ValidateId(id);
        if (provider == null) throw new ArgumentNullException(nameof(provider));

        if (_mouseButtons.ContainsKey(id))
        {
            MelonLogger.Warning($"VirtualInputs: mouse button '{id}' already registered; ignoring duplicate.");
            return Noop.Instance;
        }

        _mouseButtons[id] = new MouseButtonProvider { Id = id, Button = button, Provider = provider };
        return new MouseButtonHandle(id);
    }

    /// <summary>
    /// Registers a virtual axis. Provider's value is added to the
    /// <see cref="UnityEngine.Input.GetAxis(string)"/> result for
    /// <paramref name="axisName"/> (e.g. <c>"Horizontal"</c>, <c>"Vertical"</c>,
    /// <c>"Mouse X"</c>, <c>"Mouse Y"</c>) and clamped to <c>[-1, 1]</c>.
    /// </summary>
    public static IDisposable RegisterAxis(string id, string axisName, Func<float> provider)
    {
        ValidateId(id);
        if (string.IsNullOrEmpty(axisName)) throw new ArgumentException("Axis name must be non-empty.", nameof(axisName));
        if (provider == null) throw new ArgumentNullException(nameof(provider));

        if (_axes.ContainsKey(id))
        {
            MelonLogger.Warning($"VirtualInputs: axis '{id}' already registered; ignoring duplicate.");
            return Noop.Instance;
        }

        _axes[id] = new AxisProvider { Id = id, AxisName = axisName, Provider = provider };
        return new AxisHandle(id);
    }

    /// <summary>
    /// Removes a single provider by id. Returns false if no provider had that id.
    /// </summary>
    public static bool Unregister(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        bool removed = _buttons.Remove(id) | _mouseButtons.Remove(id) | _axes.Remove(id);
        return removed;
    }

    /// <summary>
    /// Removes every provider whose id starts with <paramref name="modId"/>
    /// followed by <c>'.'</c>. Useful for cleanup on disconnect or hot-reload.
    /// </summary>
    public static int UnregisterAllForMod(string modId)
    {
        if (string.IsNullOrEmpty(modId)) return 0;
        string prefix = modId + ".";

        int removed = 0;
        removed += RemoveByPrefix(_buttons, prefix);
        removed += RemoveByPrefix(_mouseButtons, prefix);
        removed += RemoveByPrefix(_axes, prefix);
        return removed;
    }

    /// <summary>
    /// True if any provider is currently registered for <paramref name="key"/>
    /// AND that provider is currently returning true. Used by the loader's
    /// Harmony postfix on <c>Input.GetKey</c>.
    /// </summary>
    internal static bool ResolveButtonHeld(KeyCode key)
    {
        foreach (var p in _buttons.Values)
            if (p.Key == key && p.LastFrameValue)
                return true;
        return false;
    }

    /// <summary>
    /// True if any button provider for <paramref name="key"/> transitioned
    /// from not-pressed to pressed on the most recent frame.
    /// </summary>
    internal static bool ResolveButtonDown(KeyCode key)
    {
        foreach (var p in _buttons.Values)
            if (p.Key == key && p.DownThisFrame)
                return true;
        return false;
    }

    /// <summary>
    /// True if any button provider for <paramref name="key"/> transitioned
    /// from pressed to not-pressed on the most recent frame.
    /// </summary>
    internal static bool ResolveButtonUp(KeyCode key)
    {
        foreach (var p in _buttons.Values)
            if (p.Key == key && p.UpThisFrame)
                return true;
        return false;
    }

    internal static bool ResolveMouseButtonHeld(int button)
    {
        foreach (var p in _mouseButtons.Values)
            if (p.Button == button && p.LastFrameValue)
                return true;
        return false;
    }

    internal static bool ResolveMouseButtonDown(int button)
    {
        foreach (var p in _mouseButtons.Values)
            if (p.Button == button && p.DownThisFrame)
                return true;
        return false;
    }

    internal static bool ResolveMouseButtonUp(int button)
    {
        foreach (var p in _mouseButtons.Values)
            if (p.Button == button && p.UpThisFrame)
                return true;
        return false;
    }

    /// <summary>
    /// Sums every provider's current value for <paramref name="axisName"/>
    /// (case-insensitive). Returns 0 if no provider is registered.
    /// </summary>
    internal static float ResolveAxis(string axisName)
    {
        if (string.IsNullOrEmpty(axisName)) return 0f;

        float sum = 0f;
        foreach (var a in _axes.Values)
        {
            if (string.Equals(a.AxisName, axisName, StringComparison.OrdinalIgnoreCase))
            {
                try { sum += a.Provider(); }
                catch { /* a single bad provider shouldn't break the input pipeline */ }
            }
        }
        return sum;
    }

    /// <summary>
    /// Polls every button/mouse provider once per frame, computes Down/Up
    /// transitions, and stores them on the provider records. Called by the
    /// loader's Update tick.
    /// </summary>
    internal static void TickProviders()
    {
        foreach (var p in _buttons.Values)
        {
            bool current = false;
            try { current = p.Provider(); } catch { }
            p.DownThisFrame = current && !p.LastFrameValue;
            p.UpThisFrame = !current && p.LastFrameValue;
            p.LastFrameValue = current;
        }

        foreach (var p in _mouseButtons.Values)
        {
            bool current = false;
            try { current = p.Provider(); } catch { }
            p.DownThisFrame = current && !p.LastFrameValue;
            p.UpThisFrame = !current && p.LastFrameValue;
            p.LastFrameValue = current;
        }
    }

    /// <summary>Drops every registered provider. Called by the loader on disconnect.</summary>
    internal static void Reset()
    {
        _buttons.Clear();
        _mouseButtons.Clear();
        _axes.Clear();
    }

    private static int RemoveByPrefix<T>(Dictionary<string, T> dict, string prefix)
    {
        List<string> toRemove = null;
        foreach (var key in dict.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                toRemove ??= new List<string>();
                toRemove.Add(key);
            }
        }
        if (toRemove == null) return 0;
        foreach (var key in toRemove) dict.Remove(key);
        return toRemove.Count;
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Provider id must be non-empty.", nameof(id));
        if (ModNamespace.IsValid(id) && !ModNamespace.IsReserved(id)) return;
        throw new ArgumentException(
            $"Provider id '{id}' must be a non-reserved dotted-lowercase namespace prefixed by your mod id (e.g. \"mymod.fire\").",
            nameof(id));
    }

    private sealed class ButtonHandle : IDisposable
    {
        private readonly string _id;
        private bool _disposed;
        public ButtonHandle(string id) { _id = id; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _buttons.Remove(_id);
        }
    }

    private sealed class MouseButtonHandle : IDisposable
    {
        private readonly string _id;
        private bool _disposed;
        public MouseButtonHandle(string id) { _id = id; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _mouseButtons.Remove(_id);
        }
    }

    private sealed class AxisHandle : IDisposable
    {
        private readonly string _id;
        private bool _disposed;
        public AxisHandle(string id) { _id = id; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _axes.Remove(_id);
        }
    }

    private sealed class Noop : IDisposable
    {
        public static readonly Noop Instance = new();
        public void Dispose() { }
    }
}
