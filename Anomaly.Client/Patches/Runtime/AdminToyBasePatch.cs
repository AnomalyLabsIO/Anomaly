using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2CppAdminToys;

namespace Anomaly.Client.Patches.Runtime;

[HarmonyPatch]
public static class AdminToyBasePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(AdminToyBase), nameof(AdminToyBase.OnStartClient))]
    public static void OnStartClientPostfix(AdminToyBase __instance)
    {
        if (!PatchInvocationGuard.ShouldFire(__instance, PatchInvocationGuard.AdminToyAdded)) return;
        ClientRuntime.HandleAdminToyAdded(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AdminToyBase), nameof(AdminToyBase.OnDestroy))]
    public static void OnDestroyPrefix(AdminToyBase __instance)
    {
        if (!PatchInvocationGuard.ShouldFire(__instance, PatchInvocationGuard.AdminToyRemoved)) return;
        ClientRuntime.HandleAdminToyRemoved(__instance);
    }
}
