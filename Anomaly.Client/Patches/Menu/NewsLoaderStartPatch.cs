using Anomaly.Client.Internal.UI.News;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace Anomaly.Client.Patches.Menu;

[HarmonyPatch(typeof(NewsLoader), nameof(NewsLoader.Start))]
public class NewsLoaderStartPatch
{
    private static bool _warned;

    [HarmonyPostfix]
    public static void Postfix(NewsLoader __instance)
    {
        try
        {
            // Let native Start initialize its serialized templates and request first.
            // The owned panel is independent of ArticleText, so late stock news
            // responses cannot replace diagnostic content.
            NewsDiagnosticsPanel.Install(__instance);
        }
        catch (Exception exception)
        {
            if (_warned) return;
            _warned = true;
            MelonLogger.Warning("Anomaly news diagnostics could not be loaded: " + exception.Message);
        }
    }
}
