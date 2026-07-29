using System;
using Anomaly.Server.Internal.Motion;
using HarmonyLib;
using PlayerRoles.FirstPersonControl;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Patches
{
    /// <summary>
    /// Leaves vanilla movement unchanged unless PlayerMotion owns a live,
    /// explicitly registered discontinuity for this exact motor.
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
                if (__instance?.Hub != null)
                    FpcMotionHandoff.Cancel(__instance.Hub);
                Logger.Error($"FpcMotionHandoffPatch.GetFrameMovePostfix: {ex}");
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
                if (module?.Motor?.Hub != null)
                    FpcMotionHandoff.Cancel(module.Motor.Hub);
            }
            catch (Exception ex)
            {
                Logger.Error($"FpcMotionHandoffPatch.{context}: {ex}");
            }
        }
    }
}
