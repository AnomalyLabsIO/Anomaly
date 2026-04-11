using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2Cpp;

namespace Anomaly.Client.Patches.Network;

[HarmonyPatch]
public static class CustomNetworkManagerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnStartClient))]
    public static void OnStartClientPostfix()
    {
        ClientRuntime.HandleClientStarted();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientConnect))]
    public static void OnClientConnectPostfix()
    {
        ClientRuntime.HandleClientConnected();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientDisconnect))]
    public static void OnClientDisconnectPostfix()
    {
        ClientRuntime.HandleClientDisconnected();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnClientSceneChanged))]
    public static void OnClientSceneChangedPostfix()
    {
        ClientRuntime.HandleClientReady();
    }
}
