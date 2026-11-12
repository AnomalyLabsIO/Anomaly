using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2CppInventorySystem.Items.Pickups;

namespace Anomaly.Client.Patches.Runtime;

[HarmonyPatch]
public static class ItemPickupBasePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemPickupBase), nameof(ItemPickupBase.OnStartClient))]
    public static void OnStartClientPostfix(ItemPickupBase __instance)
    {
        ClientRuntime.HandlePickupAdded(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ItemPickupBase), nameof(ItemPickupBase.OnDestroy))]
    public static void OnDestroyPrefix(ItemPickupBase __instance)
    {
        ClientRuntime.HandlePickupRemoved(__instance);
    }
}
