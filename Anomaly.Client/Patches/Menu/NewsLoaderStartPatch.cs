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
                $"<color=#ff3300>Anomaly</color>",
                "<b><size=20>Welcome to Anomaly, a SCP:SL modded version.</size></b>\n" +
                "\n<color=#ec0c02>Alpha Build</color>",
                "April 5, 2026",
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
