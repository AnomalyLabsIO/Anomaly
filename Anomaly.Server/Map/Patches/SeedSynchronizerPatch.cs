using HarmonyLib;
using MapGeneration;
using Mirror;
using NetworkManagerUtils.Dummies;

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

            // Dummies have a no-op connection and don't run client-side map
            // generation; sending bootstrap to them is wasted work.
            if (connectionToClient is DummyNetworkConnection)
                return false;

            FacilityLayout.SendBootstrap(connectionToClient);
            return false;
        }
    }
}
