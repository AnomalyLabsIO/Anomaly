using HarmonyLib;
using MapGeneration;
using Mirror;

namespace Anomaly.Server.Map.Patches
{
    /// <summary>
    /// Intercept the vanilla per-connection <c>SeedMessage</c> send so we can
    /// precede it with the facility recipe bootstrap on the same reliable
    /// Mirror connection before the client starts local map generation.
    /// </summary>
    [HarmonyPatch(typeof(SeedSynchronizer), "OnNewPlayerConnected")]
    internal static class SeedSynchronizerOnNewPlayerConnectedPatch
    {
        private static bool Prefix(NetworkConnectionToClient connectionToClient)
        {
            // Host (local player) — let vanilla run; no network hop needed.
            if (connectionToClient == NetworkClient.connection)
                return true;

            FacilityLayout.SendBootstrap(connectionToClient);
            return false;
        }
    }
}
