using Anomaly.Client.Components;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace Anomaly.Client.Patches.Menu;

[HarmonyPatch(typeof(NewServerBrowser), nameof(NewServerBrowser.OnEnable))]
public class NewServerBrowserOnEnablePatch
{
    [HarmonyPrefix]
    public static bool OnServerListEnable(NewServerBrowser __instance)
    {
        try
        {
            MelonLogger.Msg("ServerList Enable!");
            var filter = __instance.GetComponent<ServerFilter>();
            AuthStatusOverlay.Refresh();
            return true;
        }
        catch (Exception e)
        {
            MelonLogger.Msg("Patch.Il2cpp.NewServerBrowser.OnEnable: " + e);
            return true;
        }
    }
}
