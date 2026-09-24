using System;
using Anomaly.Client.Internal.Map;
using Anomaly.Client.Auth;
using HarmonyLib;
using Il2CppMapGeneration;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>Only class-reference signatures are detoured; non-blittable interpretation arguments stay native.</summary>
[HarmonyPatch(typeof(AtlasZoneGenerator), nameof(AtlasZoneGenerator.Generate))]
internal static class AtlasZoneGeneratorPatch
{
    private static bool Prefix(AtlasZoneGenerator __instance, Il2CppSystem.Random rng)
    {
        if (!AuthHandler.UsingAnomalyAuth || Il2CppMirror.NetworkServer.active) return true;
        if (!FacilityRecipeClient.IsGenerating) return false;
        var snapshot = FacilityRecipeClient.CurrentSnapshot;
        if (snapshot == null || snapshot.Mode == FacilityGenerationMode.Native) return true;
        try { FacilityGeneration.Materialize(__instance, snapshot); }
        catch (Exception exception) { FacilityRecipeClient.FailGeneration(exception); }
        return false;
    }
}
