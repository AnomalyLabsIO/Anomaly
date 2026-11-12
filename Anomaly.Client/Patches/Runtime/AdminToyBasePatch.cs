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
        ClientRuntime.HandleAdminToyAdded(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AdminToyBase), nameof(AdminToyBase.OnDestroy))]
    public static void OnDestroyPrefix(AdminToyBase __instance)
    {
        ClientRuntime.HandleAdminToyRemoved(__instance);
    }
}
