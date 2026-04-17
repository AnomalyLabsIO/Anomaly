using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;

namespace Anomaly.Client.Patches.Runtime;

[HarmonyPatch]
public static class InventoryPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.Start))]
    public static void StartPostfix(Inventory __instance)
    {
        if (!PatchInvocationGuard.ShouldFire(__instance, PatchInvocationGuard.InventoryStart)) return;
        if (__instance.isLocalPlayer)
            ClientRuntime.HandleLocalClientStarted();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.TargetRefreshItems))]
    public static void TargetRefreshItemsPostfix(Inventory __instance)
    {
        if (__instance._hub != null)
            ClientRuntime.HandleItemsModified(__instance._hub);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.OnItemUpdated))]
    public static void OnItemUpdatedPostfix(Inventory __instance, ItemIdentifier prev, ItemIdentifier cur)
    {
        if (__instance._hub != null)
            ClientRuntime.HandleCurrentItemChanged(__instance._hub, prev, cur);
    }
}
