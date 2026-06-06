using Anomaly.Client.Components;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Patches.Credits;

[HarmonyPatch(typeof(NewCredits), nameof(NewCredits.OnButtonClick))]
public class NewCreditsPatch
{
    [HarmonyPrefix]
    public static bool InjectCreditsHook()
    {
        try
        {
            if (CreditsHookComponent.Singleton != null)
                return true;

            GameObject creditsHookObject = new GameObject
            {
                name = "Credits Hook"
            };
            creditsHookObject.AddComponent<CreditsHookComponent>();
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"NewCreditsPatch: failed to inject credits hook: {ex}");
        }

        return true;
    }
}
