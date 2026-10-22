using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using HarmonyLib;
using MelonLoader;
using LockerBase = Il2CppMapGeneration.Distributors.Locker;
using LockerWrapper = Anomaly.Client.Api.Features.Locker;

namespace Anomaly.Client.Patches.Runtime;

[HarmonyPatch(typeof(LockerBase), nameof(LockerBase.DeserializeSyncVars))]
internal static class LockerStatePatch
{
    [HarmonyPrefix]
    private static void Prefix(LockerBase __instance, out ushort? __state)
    {
        __state = null;
        try { if (__instance != null) __state = __instance.NetworkOpenedChambers; }
        catch (Exception ex) { MelonLogger.Error($"LockerStatePatch.Prefix: {ex}"); }
    }

    [HarmonyPostfix]
    private static void Postfix(LockerBase __instance, bool initialState, ushort? __state)
    {
        try
        {
            if (initialState || __instance == null || !__state.HasValue)
                return;
            ushort current = __instance.NetworkOpenedChambers;
            if (current != __state.Value && LockerWrapper.Get(__instance) is { } locker)
                LockerEvents.OnStateChanged(new LockerStateChangedEventArgs(locker, __state.Value, current));
        }
        catch (Exception ex) { MelonLogger.Error($"LockerStatePatch.Postfix: {ex}"); }
    }
}
