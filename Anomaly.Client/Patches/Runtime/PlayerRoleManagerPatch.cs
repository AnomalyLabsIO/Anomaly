using Anomaly.Client.Internal;
using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2CppPlayerRoles;

namespace Anomaly.Client.Patches.Runtime;

[HarmonyPatch(typeof(PlayerRoleManager), nameof(PlayerRoleManager.InitializeNewRole))]
public static class PlayerRoleManagerPatch
{
    [HarmonyPrefix]
    public static void Prefix(PlayerRoleManager __instance, out PlayerRoleBase __state)
    {
        __state = __instance._curRole;
    }

    [HarmonyPostfix]
    public static void Postfix(PlayerRoleManager __instance, PlayerRoleBase __state)
    {
        // Always clear the silent flag after any role change, even if something went wrong
        if (__instance._hub != null && __instance._hub.isLocalPlayer)
            SilentRoleChange.IsPending = false;

        if (__instance._hub == null || ReferenceEquals(__state, __instance._curRole))
            return;

        ClientRuntime.HandleRoleChanged(__instance._hub, __state, __instance._curRole);
    }
}
