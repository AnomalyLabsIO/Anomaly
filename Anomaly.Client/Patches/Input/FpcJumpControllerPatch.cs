using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Wrappers;
using HarmonyLib;
using Il2CppPlayerRoles.FirstPersonControl;
using MelonLoader;

namespace Anomaly.Client.Patches.Input;

/// <summary>
/// Fires <see cref="PlayerEvents.Jumping"/> when <c>FpcJumpController.ProcessJump</c>
/// is about to apply a jump for the local player. Cancelling consumes the
/// jump request (sets <c>WantsToJump = false</c>) and skips the original method
/// so no jump velocity is applied this frame.
/// </summary>
[HarmonyPatch]
public static class FpcJumpControllerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FpcJumpController), nameof(FpcJumpController.ProcessJump))]
    public static bool ProcessJumpPrefix(FpcJumpController __instance, ref bool sendJump, float jumpSpeed, out float yVelocity, ref bool __result)
    {
        yVelocity = 0f;
        try
        {
            if (__instance.Hub == null || !__instance.Hub.isLocalPlayer) return true;
            if (__instance.JumpMultiplier <= 0f) return true;
            if (!__instance.WantsToJump) return true;
            if (jumpSpeed <= 0f) return true;

            Player player = PlayerRegistry.Get(__instance.Hub);
            if (player == null) return true;

            PlayerJumpingEventArgs ev = new(player, jumpSpeed);
            PlayerEvents.OnJumping(ev);
            if (ev.IsAllowed) return true;

            // Cancelled: consume the request so we don't re-fire next frame,
            // and emulate the "no jump" branch of ProcessJump.
            __instance.WantsToJump = false;
            yVelocity = -10f;
            sendJump = false;
            __result = false;
            return false;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"FpcJumpControllerPatch.ProcessJumpPrefix: {ex}");
            return true;
        }
    }
}
