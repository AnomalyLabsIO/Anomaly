using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2Cpp;
using UnityEngine.SceneManagement;

namespace Anomaly.Client.Patches.Loader;

[HarmonyPatch(typeof(LoadingScreen), nameof(LoadingScreen.OnSceneLoaded))]
public static class LoaderPatch
{
    [HarmonyPostfix]
    public static void OnSceneLoadedPostfix(Scene scene, LoadSceneMode mode)
    {
        ClientRuntime.HandleSceneLoaded(scene, mode);
    }
}
