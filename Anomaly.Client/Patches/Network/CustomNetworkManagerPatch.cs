using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2Cpp;

namespace Anomaly.Client.Patches.Network;

[HarmonyPatch]
public static class CustomNetworkManagerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnStartClient))]
    public static void OnStartClientPostfix(CustomNetworkManager __instance)
    {
        ClientRuntime.HandleClientStarted(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientConnect))]
    public static void OnClientConnectPostfix(CustomNetworkManager __instance)
    {
        ClientRuntime.HandleClientConnected(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientDisconnect))]
    public static void OnClientDisconnectPostfix(CustomNetworkManager __instance)
    {
        ClientRuntime.HandleClientDisconnected(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientSceneChanged))]
    public static void OnClientSceneChangedPostfix(CustomNetworkManager __instance)
    {
        ClientRuntime.HandleClientReady(__instance);
    }
}
