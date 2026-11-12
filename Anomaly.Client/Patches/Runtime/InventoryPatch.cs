using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Wrappers;
using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;
using MelonLoader;

namespace Anomaly.Client.Patches.Runtime;

[HarmonyPatch]
public static class InventoryPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.Start))]
    public static void StartPostfix(Inventory __instance)
    {
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

    /// <summary>
    /// Fires <see cref="InventoryEvents.DroppingItem"/> on the client-initiated
    /// drop path. Cancelling skips both the local holster-request and the
    /// <c>CmdDropItem</c> RPC — the server never sees the drop attempt.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.ClientDropItem))]
    public static bool ClientDropItemPrefix(Inventory __instance, ushort itemSerial, bool tryThrow)
    {
        try
        {
            if (__instance._hub == null) return true;
            Player player = PlayerRegistry.Get(__instance._hub);
            if (player == null) return true;

            PlayerDroppingItemEventArgs ev = new(player, itemSerial, tryThrow);
            InventoryEvents.OnDroppingItem(ev);
            return ev.IsAllowed;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"InventoryPatch.ClientDropItemPrefix: {ex}");
            return true;
        }
    }

    /// <summary>
    /// Fires <see cref="InventoryEvents.CurrentItemChanging"/> on the
    /// client-initiated held-item switch path. Server-forced switches arrive
    /// via the <c>NetworkCurItem</c> SyncVar hook (<c>OnItemUpdated</c>) and
    /// do not pass through this path, so cancelling here only affects player
    /// input, never server-driven holsters.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.ClientSelectItem))]
    public static bool ClientSelectItemPrefix(Inventory __instance, ushort itemSerial)
    {
        try
        {
            if (__instance._hub == null) return true;
            Player player = PlayerRegistry.Get(__instance._hub);
            if (player == null) return true;

            CurrentItemChangingEventArgs ev = new(player, __instance.CurItem, itemSerial);
            InventoryEvents.OnCurrentItemChanging(ev);
            return ev.IsAllowed;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"InventoryPatch.ClientSelectItemPrefix: {ex}");
            return true;
        }
    }
}
