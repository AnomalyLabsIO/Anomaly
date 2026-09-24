using System;
using Anomaly.Client.Auth;
using Anomaly.Client.Content;
using Anomaly.Client.Internal.Map;
using HarmonyLib;
using Il2CppMapGeneration;
using Il2CppMirror;

namespace Anomaly.Client.Patches.Runtime;

// A fast restart keeps the transport but replaces the player hub and its offer.
// Reset before Awake and the facility-ready postfix can release Ready/AddPlayer.
[HarmonyPatch(typeof(SeedSynchronizer), nameof(SeedSynchronizer.Awake))]
internal static class ContentSceneLifecyclePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix()
    {
        if (!AuthHandler.UsingAnomalyAuth || NetworkServer.active) return;
        try { ContentCatalogClient.BeginFacilityScene(); }
        catch (Exception exception) { FacilityRecipeClient.FailGeneration(exception); }
    }
}
