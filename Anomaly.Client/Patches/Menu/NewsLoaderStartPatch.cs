using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Internal.Localization;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace Anomaly.Client.Patches.Menu;

[HarmonyPatch(typeof(NewsLoader), nameof(NewsLoader.Start))]
public class NewsLoaderStartPatch
{
    [HarmonyPrefix]
    public static bool Prefix(NewsLoader __instance)
    {
        try
        {
            __instance._announcements = new Il2CppSystem.Collections.Generic.List<NewsLoader.Announcement>();
            __instance._announcements.Clear();
            __instance._announcements.Add(new NewsLoader.Announcement(
                Tr.Get(TrKeys.News_Anomaly_Title),
                Tr.Get(TrKeys.News_Anomaly_Body),
                Tr.Get(TrKeys.News_Anomaly_Date),
                "", GameObject.FindObjectOfType<NewsElement>()));
            __instance.ShowAnnouncement(0);
            return false;
        }
        catch(Exception)
        {
            return false;
        }
    }
}
