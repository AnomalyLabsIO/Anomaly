using Anomaly.Client.API.Assets;
using Anomaly.Client.API.Networking;
using Anomaly.Client.Assets;
using Anomaly.Client.Events.Handlers;
using Anomaly.Client.FileTransfer;
using Anomaly.Shared.Networking;
using HarmonyLib;
using Il2CppMirror;
using MelonLoader;

namespace Anomaly.Client.Patches.Mirror;

[HarmonyPatch(typeof(NetworkManager),nameof(NetworkManager.RegisterClientMessages))]
public static class NetworkManagerRegisterClientMessages
{
    private static bool _wired;

    [HarmonyPostfix]
    public static void OnRegister()
    {
        try
        {
            if (_wired) return;
            _wired = true;

            AnomalyMessageRegistry.Log = s => MelonLogger.Msg(s);
            AnomalyMessageRegistry.Warn = s => MelonLogger.Warning(s);
            AnomalyMessageRegistry.RegisterDefaults();

            SilentRoleChangeHandler.Register();
            FileCache.Init();
            FileTransferClient.Init();

            SpawnedObjectRegistry.Init();
            OverrideRegistry.Init();

            // Clean up everything the asset pipeline created so reconnecting to a
            // different server doesn't leak bundles/textures or leave stale
            // overrides bleeding onto vanilla materials.
            ClientEvents.Disconnected += _ =>
            {
                OverrideRegistry.Clear();
                SpawnedObjectRegistry.Clear();
                AnomalyResources.UnloadAll();
            };
        }
        catch(Exception e)
        {
            MelonLogger.Error("Patch.Il2CppMirror.NetworkManager.RegisterClientMessages: " + e);
        }
    }
}
