using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Wrappers;
using HarmonyLib;
using Il2Cpp;
using Il2CppInteractables;
using MelonLoader;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Fires <see cref="PlayerEvents.Interacting"/> when the local player triggers
/// <c>InteractionCoordinator.ClientInteract</c>. Cancelling skips the entire
/// method — no raycast, no local <c>IClientInteractable.ClientInteract</c>,
/// no <c>CmdServerInteract</c> RPC.
/// </summary>
[HarmonyPatch]
public static class InteractionCoordinatorPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InteractionCoordinator), "ClientInteract")]
    public static bool ClientInteractPrefix(InteractionCoordinator __instance)
    {
        try
        {
            if (!__instance.isLocalPlayer) return true;
            ReferenceHub hub = __instance._hub;
            if (hub == null) return true;

            Player player = PlayerRegistry.Get(hub);
            if (player == null) return true;

            PlayerInteractingEventArgs ev = new(player);
            PlayerEvents.OnInteracting(ev);
            return ev.IsAllowed;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"InteractionCoordinatorPatch.ClientInteractPrefix: {ex}");
            return true;
        }
    }
}
