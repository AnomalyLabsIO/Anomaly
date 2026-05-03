using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using HarmonyLib;
using Il2CppPlayerRoles.Ragdolls;
using MelonLoader;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Drives <see cref="RagdollEvents"/> spawn/remove notifications. The engine
/// doesn't expose static lifecycle events for <see cref="BasicRagdoll"/>, so
/// these postfix patches do the work instead. <see cref="Ragdoll"/>'s wrapper
/// cache also gets evicted in the OnDestroy postfix.
/// </summary>
[HarmonyPatch]
public static class BasicRagdollPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BasicRagdoll), nameof(BasicRagdoll.Start))]
    public static void Start_Postfix(BasicRagdoll __instance)
    {
        try
        {
            if (__instance == null) return;
            Ragdoll wrapper = Ragdoll.Get(__instance);
            if (wrapper != null)
                RagdollEvents.OnSpawned(new RagdollEventArgs(wrapper));
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"BasicRagdollPatch.Start_Postfix: {ex}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BasicRagdoll), nameof(BasicRagdoll.OnDestroy))]
    public static void OnDestroy_Prefix(BasicRagdoll __instance)
    {
        try
        {
            if (__instance == null) return;
            Ragdoll wrapper = Ragdoll.Get(__instance);
            if (wrapper != null)
                RagdollEvents.OnRemoved(new RagdollEventArgs(wrapper));
            Ragdoll.Unregister(__instance);
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"BasicRagdollPatch.OnDestroy_Prefix: {ex}");
        }
    }
}
