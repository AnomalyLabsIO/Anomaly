using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Assets;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.FileTransfer;
using Anomaly.Client.Internal.Map;
using Anomaly.Client.Internal.Round;
using Anomaly.Client.Patches.Audio;
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
            if (!_wired)
            {
                _wired = true;

                AnomalyMessageRegistry.Log = s => MelonLogger.Msg(s);
                AnomalyMessageRegistry.Warn = s => MelonLogger.Warning(s);
                AnomalyMessageRegistry.RegisterDefaults();

                SilentRoleChangeHandler.Register();
                FileCache.Init();
                FileTransferClient.Init();

                SpawnedObjectRegistry.Init();
                OverrideRegistry.Init();
                PathOverrideRegistry.Init();
                RoundStateReceiver.Init();

                // Clean up everything the asset pipeline created so reconnecting to a
                // different server doesn't leak bundles/textures or leave stale
                // overrides bleeding onto vanilla materials.
                ClientEvents.Disconnected += _ =>
                {
                    FacilityRecipeClient.Clear();
                    OverrideRegistry.Clear();
                    SpawnedObjectRegistry.Clear();
                    PathOverrideRegistry.Shutdown();
                    CassieLineSourceSchedulePatch.RestoreAll();
                    AnomalyResources.UnloadAll();
                    // Reset file-transfer state so a reconnect starts with a clean manifest
                    // and chunk buffer instead of leaking partially-received data or
                    // double-counting progress.
                    FileTransferClient.Reset();
                    // Drop per-channel replay state so the new session's counters
                    // start from zero instead of inheriting the old highWater.
                    AnomalyReplayTable.Reset();
                };
            }

            // NetworkClient.handlers is rebuilt on reconnect, so these plain
            // Mirror bootstrap handlers must be re-registered every time.
            FacilityRecipeClient.RegisterHandlers();
        }
        catch(Exception e)
        {
            MelonLogger.Error("Patch.Il2CppMirror.NetworkManager.RegisterClientMessages: " + e);
        }
    }
}
