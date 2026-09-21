using Anomaly.Client.Api.Input;
using UnityEngine;

namespace Anomaly.Client.Api.Internal.Input;

/// <summary>
/// Per-frame poller that evaluates registered <see cref="InputRegistry"/> bindings
/// against Unity's legacy <see cref="UnityEngine.Input"/> API, raising pressed/released
/// events on transition. Called from <c>ClientRuntime.Update</c>.
/// </summary>
/// <remarks>
/// <para>Modifier matching is strict: a binding with only Shift required will NOT
/// fire if Ctrl is also held. This prevents accidental activation of a plain-F
/// binding while the user is performing a Ctrl+F binding.</para>
/// </remarks>
internal static class InputPoller
{
    private static readonly InputFrameDispatcher Dispatcher = new();

    internal static void Update()
    {
        Dispatcher.Update(
            InputRegistry.ResolvedKeysSnapshot(),
            IsHeld,
            InputRegistry.IsCurrent,
            InputRegistry.OnPressed,
            InputRegistry.OnReleased);
    }

    internal static void Reset() => Dispatcher.Reset();

    internal static void Forget(object registration) => Dispatcher.Forget(registration);

    /// <summary>True iff the primary key is held AND every required modifier is held
    /// AND no disallowed modifier is held.</summary>
    internal static bool IsHeld(InputKey key)
    {
        if (key.Key == KeyCode.None) return false;
        if (!UnityEngine.Input.GetKey(key.Key)) return false;

        bool ctrl  = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
        bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift)   || UnityEngine.Input.GetKey(KeyCode.RightShift);
        bool alt   = UnityEngine.Input.GetKey(KeyCode.LeftAlt)     || UnityEngine.Input.GetKey(KeyCode.RightAlt);

        if (key.HasModifier(InputModifiers.Ctrl)  != ctrl)  return false;
        if (key.HasModifier(InputModifiers.Shift) != shift) return false;
        if (key.HasModifier(InputModifiers.Alt)   != alt)   return false;

        return true;
    }

    /// <summary>Transition: not pressed last frame, pressed this frame.</summary>
    internal static bool WasPressedThisFrame(InputKey key)
    {
        // GetKeyDown alone doesn't respect modifiers; we combine it with IsHeld
        // to ensure the modifier state matches on the edge.
        if (key.Key == KeyCode.None) return false;
        return UnityEngine.Input.GetKeyDown(key.Key) && IsHeld(key);
    }

    internal static bool WasReleasedThisFrame(InputKey key)
    {
        if (key.Key == KeyCode.None) return false;
        // On release we can't call IsHeld (the key is no longer down), so we
        // check only the primary key's release edge. Modifier-aware release
        // semantics would require tracking per-binding previous state, which
        // InputPoller already does in its transition loop.
        return UnityEngine.Input.GetKeyUp(key.Key);
    }
}
