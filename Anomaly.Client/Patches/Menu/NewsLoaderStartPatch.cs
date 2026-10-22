using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Internal.Localization;
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
            // Native Start initializes the carousel and asynchronous news request.
            // Its serialized element is an inactive template, not a ready thumbnail:
            // fabricating an Announcement before Start makes ShowAnnouncement throw.
            // Present local guidance through the existing article label while leaving
            // subsequent stock news loading and navigation intact.
            if (__instance == null || __instance.ArticleText == null) return;
            string version = typeof(Core).Assembly.GetName().Version?.ToString(3) ?? Tr.Get(TrKeys.News_EarlyAccess);
            __instance.ArticleText.text = Tr.Get(TrKeys.News_Anomaly_Title) + "\n"
                + Tr.Get(TrKeys.News_Anomaly_Date, version) + "\n\n"
                + Tr.Get(TrKeys.News_Anomaly_Body);
            __instance._curAnncUrl = string.Empty;
            if (__instance.OpenNewsUrlButton != null)
                __instance.OpenNewsUrlButton.interactable = false;
        }
        catch (Exception exception)
        {
            if (_warned) return;
            _warned = true;
            MelonLogger.Warning("Anomaly welcome panel could not be loaded: " + exception.Message);
        }
    }
}
