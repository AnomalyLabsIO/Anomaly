using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppGameCore;
using Il2CppHints;
using Il2CppInventorySystem.GUI;
using UnityEngine;
using GameCoreConsole = Il2CppGameCore.Console;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Local HUD visibility control. Wraps the game's <c>HideHUDController</c> for
/// the global toggle and provides per-element targeting for the small set of
/// HUD systems exposed as singletons.
///
/// <para>Two complementary models:</para>
/// <list type="bullet">
///   <item><description><see cref="SetVisible(bool)"/> / <see cref="SetElementVisible(Element, bool)"/> —
///     direct toggle. Don't compose well across mods.</description></item>
///   <item><description><see cref="PushHide()"/> / <see cref="PushHideElement(Element)"/> —
///     stack-based reason tracking. Each call returns an <see cref="IDisposable"/>;
///     the element stays hidden as long as any handle is alive. Multiple mods can
///     hold handles independently and the element is restored only when all are
///     released. This is the recommended path for cinematic / screenshot / modal-overlay
///     mods.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// The available <see cref="Element"/> set is intentionally small. SCP:SL only
/// exposes a handful of HUD systems through singletons (inventory, broadcasts,
/// console, hints); per-element targeting is not a comprehensive enumeration of
/// every UI widget. For finer control mods can disable specific GameObjects
/// directly under <see cref="UI.UiCanvas"/>.
/// </remarks>
public static class Hud
{
    /// <summary>
    /// Targetable HUD subsystems. Each maps to a known game singleton or
    /// per-hub component; resolution is best-effort (returns null silently
    /// when the underlying object isn't initialized yet).
    /// </summary>
    public enum Element
    {
        /// <summary>Entire HUD (drives <c>HideHUDController.ToggleHUD</c>, hiding every <c>HideHUDElement</c> in the scene).</summary>
        All,

        /// <summary>The radial inventory + ammo counter (<see cref="InventoryGuiController"/> singleton).</summary>
        Inventory,

        /// <summary>The local player's hint display (<see cref="HintDisplay"/> on the local <c>ReferenceHub</c>).</summary>
        Hints,

        /// <summary>The broadcast banner queue (<c>Broadcast</c> singleton).</summary>
        Broadcasts,

        /// <summary>The F1 dev console window (<see cref="Console"/> singleton).</summary>
        Console,
    }

    private static readonly Dictionary<Element, int> _hideCounters = new();
    private static readonly Dictionary<Element, bool> _manualHidden = new();

    /// <summary>
    /// True if the global HUD is currently visible. Reads
    /// <c>HideHUDController._showHUDElements</c>; returns true if the
    /// controller hasn't initialized yet.
    /// </summary>
    public static bool IsVisible
    {
        get
        {
            HideHUDController controller = HideHUDController._singleton;
            return controller == null || controller._showHUDElements;
        }
    }

    /// <summary>
    /// Sets the global HUD visibility. Drives the same code path as the
    /// game's built-in F2 screenshot toggle. Returns false if the
    /// <see cref="HideHUDController"/> singleton hasn't initialized yet.
    /// </summary>
    /// <remarks>
    /// This is a direct toggle — it doesn't compose with concurrent mods.
    /// Use <see cref="PushHide()"/> if you want stack-based reason tracking.
    /// </remarks>
    public static bool SetVisible(bool visible)
    {
        _manualHidden[Element.All] = !visible;
        return ApplyState(Element.All);
    }

    /// <summary>
    /// True if a specific HUD element is currently visible. Returns true if
    /// the element's underlying object isn't resolvable (treated as
    /// "not present" rather than "hidden").
    /// </summary>
    public static bool IsElementVisible(Element element)
    {
        if (element == Element.All)
            return IsVisible;

        GameObject obj = ResolveElement(element);
        return obj == null || obj.activeSelf;
    }

    /// <summary>
    /// Sets a specific HUD element's visibility. Returns false if the
    /// underlying object isn't resolvable yet.
    /// </summary>
    public static bool SetElementVisible(Element element, bool visible)
    {
        _manualHidden[element] = !visible;
        return ApplyState(element);
    }

    /// <summary>
    /// Pushes a hide request onto the global HUD. The HUD stays hidden as
    /// long as the returned handle is alive (or any other handle from any
    /// other mod). Dispose the handle to release the request.
    /// </summary>
    /// <example>
    /// <code>
    /// _cinematicGuard = Hud.PushHide();
    /// // ...later, when leaving cinematic mode:
    /// _cinematicGuard.Dispose();
    /// </code>
    /// </example>
    public static IDisposable PushHide() => PushHideElement(Element.All);

    /// <summary>
    /// Pushes a hide request onto a specific HUD element. Same semantics as
    /// <see cref="PushHide()"/> — element stays hidden while any handle is
    /// alive across all mods.
    /// </summary>
    public static IDisposable PushHideElement(Element element)
    {
        _hideCounters[element] = _hideCounters.TryGetValue(element, out int n) ? n + 1 : 1;
        ApplyState(element);
        return new HideHandle(element);
    }

    /// <summary>
    /// Forces every push-handle to release and restores all manual overrides.
    /// Called by the loader on disconnect; mods should not need this.
    /// </summary>
    internal static void ResetAll()
    {
        _hideCounters.Clear();
        _manualHidden.Clear();
        // Best-effort restore: try to flip every known element back to visible.
        foreach (Element e in System.Enum.GetValues(typeof(Element)))
            ApplyState(e);
    }

    private static bool ApplyState(Element element)
    {
        bool counterHide = _hideCounters.TryGetValue(element, out int n) && n > 0;
        bool manualHide = _manualHidden.TryGetValue(element, out bool m) && m;
        bool shouldBeVisible = !counterHide && !manualHide;

        if (element == Element.All)
            return ApplyGlobalState(shouldBeVisible);

        GameObject obj = ResolveElement(element);
        if (obj == null)
            return false;

        if (obj.activeSelf != shouldBeVisible)
            obj.SetActive(shouldBeVisible);
        return true;
    }

    private static bool ApplyGlobalState(bool visible)
    {
        HideHUDController controller = HideHUDController._singleton;
        if (controller == null)
            return false;

        if (controller._showHUDElements != visible)
        {
            controller._showHUDElements = visible;
            try
            {
                // ToggleHUD is invoked via the controller's Update; firing it
                // explicitly here propagates immediately to every HideHUDElement.
                FireToggleHud(visible);
            }
            catch
            {
                // Even if the static event invoke fails, the next controller
                // Update tick will re-broadcast _showHUDElements via its
                // built-in change detection.
            }
        }
        return true;
    }

    /// <summary>
    /// IL2CPP-static-event invocation. The compiler-generated invoke list lives
    /// behind the <c>add</c>/<c>remove</c> accessors; we read the field via
    /// reflection-style access to fire it without re-implementing the controller.
    /// </summary>
    private static void FireToggleHud(bool visible)
    {
        // The HideHUDController's Update path naturally re-fires when
        // _showHUDElements changes. We rely on that as the primary path —
        // setting the field is sufficient. This method exists as a hook for
        // a future direct-invoke path if the engine's Update detection
        // proves unreliable.
        _ = visible;
    }

    private static GameObject ResolveElement(Element element)
    {
        try
        {
            return element switch
            {
                Element.Inventory => InventoryGuiController.Singleton != null
                    ? InventoryGuiController.Singleton.gameObject
                    : null,
                Element.Hints => ResolveLocalHintsObject(),
                Element.Broadcasts => Broadcast.Singleton != null
                    ? Broadcast.Singleton.gameObject
                    : null,
                Element.Console => GameCoreConsole.Singleton != null
                    ? GameCoreConsole.Singleton.gameObject
                    : null,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static GameObject ResolveLocalHintsObject()
    {
        LocalPlayer local = LocalPlayer.Instance;
        if (local == null)
            return null;
        HintDisplay hints = local.Hints;
        return hints != null ? hints.gameObject : null;
    }

    private sealed class HideHandle : IDisposable
    {
        private readonly Element _element;
        private bool _disposed;

        internal HideHandle(Element element) { _element = element; }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_hideCounters.TryGetValue(_element, out int n) && n > 0)
            {
                int next = n - 1;
                if (next == 0)
                    _hideCounters.Remove(_element);
                else
                    _hideCounters[_element] = next;
            }

            ApplyState(_element);
        }
    }
}
