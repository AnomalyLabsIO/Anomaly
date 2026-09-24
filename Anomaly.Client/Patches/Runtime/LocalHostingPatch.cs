using Anomaly.Client.Internal.Hosting;
using HarmonyLib;
using Il2Cpp;
using Il2CppMirror;

namespace Anomaly.Client.Patches.Runtime;

[HarmonyPatch]
internal static class LocalHostingPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.StartNondedicated))]
    private static bool StartNondedicatedPrefix() => LocalHostingRuntime.StartLocalHost();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerRoles), nameof(ServerRoles.Start))]
    private static void ServerRolesStartPostfix(ServerRoles __instance) =>
        LocalHostingRuntime.ScheduleAdminGrant(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ReferenceHub), nameof(ReferenceHub.OnDestroy))]
    private static void ReferenceHubDestroyPrefix(ReferenceHub __instance) =>
        LocalHostingRuntime.RemovePlayer(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientDisconnect))]
    private static void ClientDisconnectPrefix() => LocalHostingRuntime.Reset();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StopHost))]
    private static void StopHostPrefix() => LocalHostingRuntime.Reset();
}
