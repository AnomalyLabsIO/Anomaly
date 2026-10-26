using Anomaly.Client.Internal.Map;
using Anomaly.Client.Auth;
using Anomaly.Client.Content;
using HarmonyLib;
using Il2CppMapGeneration;
using Il2CppMirror;

namespace Anomaly.Client.Patches.Runtime;

// SeedMessage is a verified blittable, four-byte struct in the generated reference.
// It does not use the non-blittable AtlasInterpretation trampoline conversion.
[HarmonyPatch(typeof(SeedSynchronizer), nameof(SeedSynchronizer.ClientReceiveSeed))]
internal static class AuthenticatedFacilitySeedPatch
{
    private static bool Prefix(SeedSynchronizer.SeedMessage msg)
    {
        if (!AuthHandler.UsingAnomalyAuth || NetworkServer.active || FacilityRecipeClient.IsGenerating) return true;
        FacilityRecipeClient.ReceiveSeed(msg.Value);
        return false;
    }
}

[HarmonyPatch(typeof(SeedSynchronizer), nameof(SeedSynchronizer.Awake))]
internal static class FacilitySceneReadyPatch
{
    private static void Postfix() { if (AuthHandler.UsingAnomalyAuth && !NetworkServer.active) FacilityRecipeClient.SceneAvailable(); }
}

[HarmonyPatch(typeof(SeedSynchronizer), nameof(SeedSynchronizer.OnDestroy))]
internal static class FacilitySceneDestroyedPatch
{
    private static void Postfix() { if (AuthHandler.UsingAnomalyAuth && !NetworkServer.active) FacilityRecipeClient.SceneDestroyed(); }
}

[HarmonyPatch(typeof(SeedSynchronizer), nameof(SeedSynchronizer.GenerateLevel))]
internal static class FacilityGenerateLevelGuardPatch
{
    private static bool Prefix() => !AuthHandler.UsingAnomalyAuth || NetworkServer.active || FacilityRecipeClient.IsGenerating;
    private static void Postfix()
    {
        if (AuthHandler.UsingAnomalyAuth && FacilityRecipeClient.IsGenerating) FacilityGenerationDiagnostics.Record();
    }
}

[HarmonyPatch(typeof(NetworkClient), nameof(NetworkClient.Ready))]
internal static class FacilityReadyGatePatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!ConnectionContentBootstrap.DeferReady()) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(NetworkClient), nameof(NetworkClient.AddPlayer))]
internal static class FacilityAddPlayerGatePatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!ConnectionContentBootstrap.DeferPlayer()) return true;
        __result = false;
        return false;
    }
}
