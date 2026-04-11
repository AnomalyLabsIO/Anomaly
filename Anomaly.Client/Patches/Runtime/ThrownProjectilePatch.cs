using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2CppInventorySystem.Items.ThrowableProjectiles;

namespace Anomaly.Client.Patches.Runtime;

[HarmonyPatch(typeof(ThrownProjectile), nameof(ThrownProjectile.Start))]
public static class ThrownProjectilePatch
{
    [HarmonyPostfix]
    public static void StartPostfix(ThrownProjectile __instance)
    {
        ClientRuntime.HandleProjectileSpawned(__instance);
    }
}
