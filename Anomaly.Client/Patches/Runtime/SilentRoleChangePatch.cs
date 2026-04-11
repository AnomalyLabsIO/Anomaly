using Anomaly.Client.Internal;
using HarmonyLib;
using Il2Cpp;
using Il2CppPlayerRoles;
using Il2CppPlayerRoles.RoleHelp;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Suppresses the role info overlay and alone soundtrack reset when a silent role change is pending.
/// </summary>
public static class SilentRoleChangePatch
{
    [HarmonyPatch(typeof(RoleHelpMenu), "OnRoleChanged")]
    public static class RoleHelpMenuPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RoleHelpMenu __instance, ReferenceHub hub)
        {
            if (!hub.isLocalPlayer || !SilentRoleChange.IsPending) return;

            // Original method already cleaned up old instance and created new one.
            // Destroy the new one immediately so the player never sees it.
            if (__instance._instanceSet)
            {
                UnityEngine.Object.Destroy(__instance._instance);
                __instance._instanceSet = false;
            }
            __instance.IsEnabled = false;
        }
    }

    [HarmonyPatch(typeof(Il2CppFacilitySoundtrack.AloneSoundtrack), "OnRoleChanged")]
    public static class AloneSoundtrackPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ReferenceHub userHub)
        {
            // Skip the alone soundtrack reset when a silent role change is pending
            return !(userHub.isLocalPlayer && SilentRoleChange.IsPending);
        }
    }
}
