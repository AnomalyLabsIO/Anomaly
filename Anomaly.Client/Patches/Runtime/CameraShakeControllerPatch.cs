using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using HarmonyLib;
using Il2CppCameraShaking;
using MelonLoader;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Fires <see cref="CameraEvents.Shaking"/> before a shake effect is enqueued
/// on the local camera. Cancelling drops the effect — no shake is rendered
/// for this client.
/// </summary>
[HarmonyPatch]
public static class CameraShakeControllerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CameraShakeController), nameof(CameraShakeController.AddEffect))]
    public static bool AddEffectPrefix(IShakeEffect effect)
    {
        try
        {
            CameraShakingEventArgs ev = new(effect);
            CameraEvents.OnShaking(ev);
            return ev.IsAllowed;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"CameraShakeControllerPatch.AddEffectPrefix: {ex}");
            return true;
        }
    }
}
