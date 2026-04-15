using Anomaly.Client.API.Networking;
using Anomaly.Client.FileTransfer;
using Anomaly.Shared.Networking;
using HarmonyLib;
using Il2CppMirror;
using MelonLoader;

namespace Anomaly.Client.Patches.Mirror;

[HarmonyPatch(typeof(NetworkManager),nameof(NetworkManager.RegisterClientMessages))]
public static class NetworkManagerRegisterClientMessages
{
    [HarmonyPostfix]
    public static void OnRegister()
    {
        try
        {
            AnomalyMessageRegistry.Log = s => MelonLogger.Msg(s);
            AnomalyMessageRegistry.Warn = s => MelonLogger.Warning(s);
            AnomalyMessageRegistry.RegisterDefaults();

            SilentRoleChangeHandler.Register();
            FileCache.Init();
            FileTransferClient.Init();
        }
        catch(Exception e)
        {
            MelonLogger.Error("Patch.Il2CppMirror.NetworkManager.RegisterClientMessages: " + e);
        }
    }
}
