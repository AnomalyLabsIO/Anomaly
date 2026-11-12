using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2Cpp;

namespace Anomaly.Client.Patches.Runtime;

[HarmonyPatch]
public static class ReferenceHubPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ReferenceHub), nameof(ReferenceHub.Start))]
    public static void StartPostfix(ReferenceHub __instance)
    {
        ClientRuntime.HandlePlayerAdded(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ReferenceHub), nameof(ReferenceHub.OnDestroy))]
    public static void OnDestroyPrefix(ReferenceHub __instance)
    {
        ClientRuntime.HandlePlayerRemoved(__instance);
    }
}
