using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2CppPlayerStatsSystem;

namespace Anomaly.Client.Patches.Runtime;

[HarmonyPatch]
public static class PlayerStatsPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.DealDamage))]
    public static void DealDamagePostfix(PlayerStats __instance, DamageHandlerBase handler, bool __result)
    {
        if (__result && __instance._hub != null)
            ClientRuntime.HandlePlayerDamaged(__instance._hub, handler);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.KillPlayer))]
    public static void KillPlayerPostfix(PlayerStats __instance, DamageHandlerBase handler)
    {
        if (__instance._hub != null)
            ClientRuntime.HandlePlayerDied(__instance._hub, handler);
    }
}
