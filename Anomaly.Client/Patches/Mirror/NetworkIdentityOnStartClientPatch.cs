using Anomaly.Client.Assets;
using HarmonyLib;
using Il2CppMirror;
using MelonLoader;

namespace Anomaly.Client.Patches.Mirror;

/// <summary>
/// Re-resolve any pending texture overrides whenever a new NetworkIdentity
/// becomes available on the client. Handles the case where the server sent an
/// override before the target object had spawned locally.
/// </summary>
[HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.OnStartClient))]
public static class NetworkIdentityOnStartClientPatch
{
    [HarmonyPostfix]
    public static void Postfix(NetworkIdentity __instance)
    {
        try
        {
            OverrideRegistry.ReapplyForNetId(__instance.netId);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"NetworkIdentityOnStartClientPatch: {ex}");
        }
    }
}
