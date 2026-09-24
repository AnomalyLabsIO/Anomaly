using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Anomaly.Server.Internal.Map;
using HarmonyLib;
using MapGeneration;
using Mirror;
using Anomaly.Server.Networking;

namespace Anomaly.Server.Patches.Map;

[HarmonyPatch(typeof(SeedSynchronizer), "Awake")]
internal static class FacilityAuthoringPatch
{
    private static void Prefix() => FacilityLayoutRuntime.BeginAuthoring();
}

[HarmonyPatch(typeof(SeedSynchronizer), "GenerateLevel")]
internal static class FacilityFreezePatch
{
    private static bool Prefix()
    {
        try { FacilityLayoutRuntime.Freeze(); return !FacilityLayoutRuntime.GenerationFailed; }
        catch (Exception exception) { FacilityLayoutRuntime.FailGeneration(exception); return false; }
    }
    private static void Postfix()
    {
        if (FacilityLayoutRuntime.GenerationFailed) return;
        try { FacilityLayoutRuntime.Publish(); FacilityGenerationDiagnostics.Record(); }
        catch (Exception exception) { FacilityLayoutRuntime.FailGeneration(exception); }
    }
    private static Exception Finalizer(Exception __exception)
    {
        if (__exception != null) FacilityLayoutRuntime.FailGeneration(__exception);
        return __exception;
    }
}

// Preserve native OnBeforeGenerated and derived zone wrappers. Only the first
// zone call resolves the complete plan; no prefab is instantiated by that pass.
[HarmonyPatch(typeof(SeedSynchronizer), "GenerateFacility")]
internal static class FacilityPlanningPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var generate = AccessTools.Method(typeof(ZoneGenerator), nameof(ZoneGenerator.Generate));
        var replacement = AccessTools.Method(typeof(FacilityPlanningPatch), nameof(GenerateZone));
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(generate))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 1) throw new InvalidOperationException("The supported facility generation loop could not be located.");
    }

    private static void GenerateZone(ZoneGenerator generator, System.Random rng)
    {
        if (FacilityLayoutRuntime.GenerationFailed) return;
        try
        {
            FacilityLayoutRuntime.PreparePlan(SeedSynchronizer._singleton._zoneGenerators, rng);
            generator.Generate(rng);
        }
        catch (Exception exception) { FacilityLayoutRuntime.FailGeneration(exception); throw; }
    }

    // The game catches exceptions inside its zone loop. This check is outside
    // that catch and prevents connectors/MapGenerated from accepting a partial map.
    private static void Postfix() => FacilityLayoutRuntime.ThrowIfFailed();
}

[HarmonyPatch(typeof(SeedSynchronizer), "OnNewPlayerConnected")]
internal static class AuthenticatedFacilityBootstrapPatch
{
    private static bool Prefix(NetworkConnectionToClient connectionToClient) =>
        connectionToClient == NetworkClient.connection || ConnectionBootstrap.TrySendCurrentSnapshot(connectionToClient);
}
