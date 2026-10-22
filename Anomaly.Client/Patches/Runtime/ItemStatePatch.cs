using Anomaly.Client.Internal.Items;
using HarmonyLib;
using Il2CppInventorySystem.Items;
using Il2CppInventorySystem.Items.Usables;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>The game has a status event but no cooldown event; observe its receive handler after application.</summary>
[HarmonyPatch]
internal static class ItemStatePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UsableItemsController), nameof(UsableItemsController.ClientReceivedCooldown))]
    private static void Postfix(ItemCooldownMessage msg) => ItemStateRuntime.HandleCooldown(msg);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ItemBase), nameof(ItemBase.OnDestroy))]
    private static void DestroyPrefix(ItemBase __instance) => ItemStateRuntime.ForgetItem(__instance);
}
