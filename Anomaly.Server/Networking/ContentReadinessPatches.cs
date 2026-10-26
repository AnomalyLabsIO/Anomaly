using HarmonyLib;
using Mirror;
using UnityEngine;

namespace Anomaly.Server.Networking;

/// <summary>Rejects premature vanilla readiness before player instantiation and at its lower-level entry points.</summary>
internal static class ContentReadinessPatches
{
    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.OnServerReady))]
    private static class ReadyMessagePatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(NetworkConnectionToClient conn) => ConnectionContentBootstrap.RequireComplete(conn);
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.OnServerAddPlayer))]
    private static class AddPlayerMessagePatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(NetworkConnectionToClient conn) => ConnectionContentBootstrap.RequireComplete(conn);
    }

    [HarmonyPatch(typeof(NetworkServer), nameof(NetworkServer.SetClientReady))]
    private static class SetReadyPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(NetworkConnectionToClient conn) => ConnectionContentBootstrap.RequireComplete(conn);
    }

    [HarmonyPatch(typeof(NetworkServer), nameof(NetworkServer.AddPlayerForConnection),
        new[] { typeof(NetworkConnectionToClient), typeof(GameObject) })]
    private static class AddPlayerPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(NetworkConnectionToClient conn, ref bool __result)
        {
            if (ConnectionContentBootstrap.RequireComplete(conn)) return true;
            __result = false;
            return false;
        }
    }
}
