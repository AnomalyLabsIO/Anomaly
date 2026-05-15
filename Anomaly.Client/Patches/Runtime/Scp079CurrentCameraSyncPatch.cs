using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Wrappers;
using HarmonyLib;
using Il2Cpp;
using Il2CppPlayerRoles.PlayableScps.Scp079.Cameras;
using MelonLoader;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Fires <see cref="FacilityCameraEvents.Scp079Switching"/> when the local
/// SCP-079 calls <c>ClientSwitchTo</c>, before the camera-switch command is
/// queued and sent. Cancelling skips both the local switch state transition
/// and the network command — the SCP-079 stays on the current camera.
/// </summary>
[HarmonyPatch]
public static class Scp079CurrentCameraSyncPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Scp079CurrentCameraSync), nameof(Scp079CurrentCameraSync.ClientSwitchTo))]
    public static bool ClientSwitchToPrefix(Scp079CurrentCameraSync __instance, Scp079Camera target)
    {
        try
        {
            if (!__instance.Role.IsControllable) return true;
            if (__instance.CurClientSwitchState != Scp079CurrentCameraSync.ClientSwitchState.None) return true;

            // Local 079 player owns this subroutine — IsControllable above already implies the local player.
            if (!ReferenceHub.TryGetLocalHub(out ReferenceHub localHub)) return true;
            Player player = PlayerRegistry.Get(localHub);
            if (player == null) return true;

            FacilityCamera wrapper = FacilityCamera.Get(target);
            if (wrapper == null) return true;

            Scp079SwitchingCameraEventArgs ev = new(player, wrapper);
            FacilityCameraEvents.OnScp079Switching(ev);
            return ev.IsAllowed;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"Scp079CurrentCameraSyncPatch.ClientSwitchToPrefix: {ex}");
            return true;
        }
    }
}
