using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Wrappers;
using HarmonyLib;
using Il2Cpp;
using Il2CppPlayerRoles.FirstPersonControl;
using MelonLoader;
using UnityInput = UnityEngine.Input;
using UnityEngine;

namespace Anomaly.Client.Patches.Input;

/// <summary>
/// Fires <see cref="PlayerEvents.NoclipToggling"/> when the local player
/// presses the noclip toggle key, before <c>UpdateNoclip</c> sends the
/// <c>FpcNoclipToggleMessage</c> to the server. Cancelling skips the entire
/// <c>UpdateNoclip</c> body for that frame — the server doesn't see the
/// toggle attempt.
/// </summary>
[HarmonyPatch]
public static class FpcNoclipPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FpcNoclip), nameof(FpcNoclip.UpdateNoclip))]
    public static bool UpdateNoclipPrefix(FpcNoclip __instance)
    {
        try
        {
            ReferenceHub hub = __instance._hub;
            if (hub == null || !hub.isLocalPlayer) return true;
            if (!UnityInput.GetKeyDown(FpcNoclip._keyToggle)) return true;

            Player player = PlayerRegistry.Get(hub);
            if (player == null) return true;

            PlayerNoclipTogglingEventArgs ev = new(player);
            PlayerEvents.OnNoclipToggling(ev);
            return ev.IsAllowed;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"FpcNoclipPatch.UpdateNoclipPrefix: {ex}");
            return true;
        }
    }
}
