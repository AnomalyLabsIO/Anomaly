using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Wrappers;
using HarmonyLib;
using Il2CppInventorySystem.GUI;
using MelonLoader;
using UnityEngine;
using UnityInput = UnityEngine.Input;
using ApiPlayer = Anomaly.Client.Api.Features.Player;
using IlThrowableItem = Il2CppInventorySystem.Items.ThrowableProjectiles.ThrowableItem;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Fires <see cref="ProjectileEvents.Throwing"/> when the local player presses
/// the throw key while a throwable is idle — i.e. when
/// <c>ThrowableItem.ClientUpdateIdle</c> is about to call
/// <c>NetworkClient.Send(BeginThrow)</c>. Cancelling skips the begin-throw
/// network message; the throw stopwatch stays at zero and no animation starts.
///
/// We replicate the inner gate (<c>ItemsSafeForInteraction</c> + key-down +
/// no <c>ItemPrimaryAction</c> block) so the event fires only on the genuine
/// throw-initiation frame, not every tick the method runs.
/// </summary>
[HarmonyPatch]
public static class ThrowableItemPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(IlThrowableItem), "ClientUpdateIdle")]
    public static bool ClientUpdateIdlePrefix(IlThrowableItem __instance)
    {
        try
        {
            if (!InventoryGuiController.ItemsSafeForInteraction) return true;
            if (!UnityInput.GetKeyDown(__instance._primaryKey) && !UnityInput.GetKeyDown(__instance._secondaryKey)) return true;
            if (__instance.Owner == null) return true;

            ApiPlayer player = PlayerRegistry.Get(__instance.Owner);
            if (player == null) return true;

            ThrowableItem item = null;
            if(ThrowableItem.TryGet(__instance.ItemSerial, out var wrapper) && wrapper is ThrowableItem throwableItem)
            {
                item = throwableItem;
            }
            PlayerThrowingEventArgs ev = new(player, item);
            ProjectileEvents.OnThrowing(ev);
            return ev.IsAllowed;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"ThrowableItemPatch.ClientUpdateIdlePrefix: {ex}");
            return true;
        }
    }
}
