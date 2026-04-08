using Anomaly.Client.Events.Args;
using Anomaly.Client.Events.Handlers;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace Anomaly.Client.Patches.Menu;

[HarmonyPatch(typeof(NewMainMenu),nameof(NewMainMenu.Start))]
public static class CanvasPatch
{
    /*[HarmonyPostfix]
    public static void OnMenuStartLate()
    {
        var ev = new CanvasReadyEventArgs(GameObject.Find("Canvas"));
        ClientEvents.OnCanvasReady(ev);
    }*/
}