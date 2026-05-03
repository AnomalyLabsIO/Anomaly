using Anomaly.Client.Api.Input;
using HarmonyLib;
using UnityEngine;

namespace Anomaly.Client.Patches.Input;

/// <summary>
/// Harmony postfixes that route <see cref="UnityEngine.Input"/> calls through
/// <see cref="VirtualInputs"/> so mod-registered providers can OR/sum into the
/// engine's input state without each mod re-patching these methods themselves.
/// </summary>
/// <remarks>
/// All patches are postfixes so vanilla input still wins when present — virtual
/// providers only contribute additively. Axes are clamped to <c>[-1, 1]</c>
/// after merging.
/// </remarks>
[HarmonyPatch]
public static class VirtualInputPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetKey), new[] { typeof(KeyCode) })]
    public static void GetKey_Postfix(KeyCode key, ref bool __result)
    {
        if (__result) return;
        if (VirtualInputs.ResolveButtonHeld(key))
            __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetKeyDown), new[] { typeof(KeyCode) })]
    public static void GetKeyDown_Postfix(KeyCode key, ref bool __result)
    {
        if (__result) return;
        if (VirtualInputs.ResolveButtonDown(key))
            __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetKeyUp), new[] { typeof(KeyCode) })]
    public static void GetKeyUp_Postfix(KeyCode key, ref bool __result)
    {
        if (__result) return;
        if (VirtualInputs.ResolveButtonUp(key))
            __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetMouseButton))]
    public static void GetMouseButton_Postfix(int button, ref bool __result)
    {
        if (__result) return;
        if (VirtualInputs.ResolveMouseButtonHeld(button))
            __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetMouseButtonDown))]
    public static void GetMouseButtonDown_Postfix(int button, ref bool __result)
    {
        if (__result) return;
        if (VirtualInputs.ResolveMouseButtonDown(button))
            __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetMouseButtonUp))]
    public static void GetMouseButtonUp_Postfix(int button, ref bool __result)
    {
        if (__result) return;
        if (VirtualInputs.ResolveMouseButtonUp(button))
            __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetAxis), new[] { typeof(string) })]
    public static void GetAxis_Postfix(string axisName, ref float __result)
    {
        float v = VirtualInputs.ResolveAxis(axisName);
        if (v == 0f) return;
        __result = Mathf.Clamp(__result + v, -1f, 1f);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetAxisRaw), new[] { typeof(string) })]
    public static void GetAxisRaw_Postfix(string axisName, ref float __result)
    {
        float v = VirtualInputs.ResolveAxis(axisName);
        if (v == 0f) return;
        __result = Mathf.Clamp(__result + v, -1f, 1f);
    }
}
