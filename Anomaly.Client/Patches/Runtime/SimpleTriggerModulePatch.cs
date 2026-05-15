using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Wrappers;
using HarmonyLib;
using Il2CppInventorySystem.Items.Firearms.Modules;
using MelonLoader;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Fires <see cref="FirearmEvents.Shooting"/> on the rising edge of the local
/// player's trigger press, before <c>SimpleTriggerModule</c> calls
/// <c>SendCmd</c> to inform the server. Cancelling skips the entire
/// <c>EquipUpdate</c> body for that frame (which also skips the trigger-state
/// update, so subsequent frames continue to see a not-pressed trigger until
/// the player releases and presses again).
///
/// Trade-off: when cancelled, the implicit <c>base.EquipUpdate()</c> call at
/// the top of the method is also skipped. ModuleBase lifecycle work is
/// generally idempotent across frames, so one skipped frame is acceptable.
/// </summary>
[HarmonyPatch]
public static class SimpleTriggerModulePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SimpleTriggerModule), nameof(SimpleTriggerModule.EquipUpdate))]
    public static bool EquipUpdatePrefix(SimpleTriggerModule __instance)
    {
        try
        {
            if (!__instance.IsLocalPlayer) return true;

            bool triggerStatus = __instance.TriggerHeld;
            if (!triggerStatus) return true;                       // releases / idle: natural flow
            if (__instance._clientData.IsHeld) return true;        // already held: no state change

            // Rising edge: trigger just pressed this frame.
            Player player = PlayerRegistry.Get(__instance.Firearm?.Owner);
            if (player == null) return true;

            PlayerShootingEventArgs ev = new(player);
            FirearmEvents.OnShooting(ev);
            return ev.IsAllowed;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"SimpleTriggerModulePatch.EquipUpdatePrefix: {ex}");
            return true;
        }
    }
}
