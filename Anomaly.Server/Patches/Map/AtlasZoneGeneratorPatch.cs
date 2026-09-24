using System;
using Anomaly.Server.Internal.Map;
using HarmonyLib;
using MapGeneration;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Server.Patches.Map;

[HarmonyPatch(typeof(AtlasZoneGenerator), nameof(AtlasZoneGenerator.Generate))]
internal static class AtlasZoneGeneratorGeneratePatch
{
    private static bool Prefix(AtlasZoneGenerator __instance, System.Random rng)
    {
        if (FacilityLayoutRuntime.GenerationFailed) return false;
        var snapshot = FacilityLayoutRuntime.CurrentSnapshot;
        if (snapshot == null || snapshot.Mode == FacilityGenerationMode.Native) return true;
        try { FacilityGeneration.Materialize(__instance, snapshot); }
        catch (Exception exception) { FacilityLayoutRuntime.FailGeneration(exception); }
        return false;
    }
}
