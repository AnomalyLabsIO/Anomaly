using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Wrappers;
using HarmonyLib;
using Il2CppPlayerRoles.FirstPersonControl;
using MelonLoader;

namespace Anomaly.Client.Patches.Input;

/// <summary>
/// Fires <see cref="PlayerEvents.Sprinting"/> when <c>FpcStateProcessor.ClientUpdateInput</c>
/// is about to set the local movement state to <c>Sprinting</c>. Fires only on
/// the transition into sprinting (not every frame sprint is held), so
/// subscribers aren't spammed.
///
/// Cancelling forces the state to walking on that frame. The server only
/// observes resulting movement state, so suppressing the local request is
/// desync-safe.
/// </summary>
[HarmonyPatch]
public static class FpcStateProcessorPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(FpcStateProcessor), nameof(FpcStateProcessor.ClientUpdateInput))]
    public static void ClientUpdateInputPostfix(FpcStateProcessor __instance, FirstPersonMovementModule moduleRef, ref PlayerMovementState valueToSend)
    {
        try
        {
            // Only act on the transition INTO Sprinting.
            if (valueToSend != PlayerMovementState.Sprinting) return;
            if (moduleRef.CurrentMovementState == PlayerMovementState.Sprinting) return;
            if (__instance.Hub == null || !__instance.Hub.isLocalPlayer) return;

            Player player = PlayerRegistry.Get(__instance.Hub);
            if (player == null) return;

            PlayerSprintingEventArgs ev = new(player);
            PlayerEvents.OnSprinting(ev);
            if (!ev.IsAllowed)
            {
                valueToSend = PlayerMovementState.Walking;
                moduleRef.CurrentMovementState = PlayerMovementState.Walking;
            }
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"FpcStateProcessorPatch.ClientUpdateInputPostfix: {ex}");
        }
    }
}
