using Anomaly.Client.Internal.Motion;
using HarmonyLib;
using Il2CppPlayerRoles.FirstPersonControl;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Patches.Motion;

/// <summary>
/// Applies only handoffs explicitly registered through LocalPlayerMotion.
/// Vanilla frame movement is untouched when no owner has an active handoff.
/// </summary>
[HarmonyPatch]
internal static class FpcMotionHandoffPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(FpcMotor), nameof(FpcMotor.GetFrameMove))]
    private static void GetFrameMovePostfix(FpcMotor __instance, ref Vector3 __result)
    {
        try
        {
            FpcMotionHandoff.ApplyFrame(__instance, ref __result);
        }
        catch (Exception ex)
        {
            FpcMotionHandoff.Cancel((string)null);
            MelonLogger.Error($"FpcMotionHandoffPatch.GetFrameMovePostfix: {ex}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FirstPersonMovementModule), "OnRoleDisabled")]
    private static void OnRoleDisabledPostfix(FirstPersonMovementModule __instance)
    {
        CancelModule(__instance, nameof(OnRoleDisabledPostfix));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FirstPersonMovementModule), nameof(FirstPersonMovementModule.ResetObject))]
    private static void ResetObjectPostfix(FirstPersonMovementModule __instance)
    {
        CancelModule(__instance, nameof(ResetObjectPostfix));
    }

    private static void CancelModule(FirstPersonMovementModule module, string context)
    {
        try
        {
            FpcMotionHandoff.Cancel(module);
        }
        catch (Exception ex)
        {
            FpcMotionHandoff.Cancel((string)null);
            MelonLogger.Error($"FpcMotionHandoffPatch.{context}: {ex}");
        }
    }
}
