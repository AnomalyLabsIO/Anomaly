using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace Anomaly.Client.Patches.Menu;

[HarmonyPatch(typeof(NewMainMenu),nameof(NewMainMenu.Start))]
public static class CanvasPatch
{
    [HarmonyPostfix]
    public static void OnMenuStartLate(NewMainMenu __instance)
    {
        GameObject canvas = UnityEngine.Object.FindObjectOfType<Canvas>()?.gameObject ?? __instance.gameObject;
        var ev = new CanvasReadyEventArgs(canvas);
        ClientEvents.OnCanvasReady(ev);
    }
}
