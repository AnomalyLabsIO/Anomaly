using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace Anomaly.Client.Patches.Loader;

/// <summary>
/// Appends " (MODDED)" to the PreLoader scene's version label
/// (e.g. "RELEASE 14.2.6" → "RELEASE 14.2.6 (MODDED)"). Postfix on
/// <c>LauncherAssetScanProgressBar.Awake</c> runs right after the original
/// sets the label text.
/// </summary>
[HarmonyPatch(typeof(LauncherAssetScanProgressBar), "Awake")]
public static class LauncherVersionPatch
{
    private const string ModSuffix = " (MODDED)";

    [HarmonyPostfix]
    public static void Postfix(LauncherAssetScanProgressBar __instance)
    {
        try
        {
            var label = __instance._version;
            if (label == null) return;

            string current = label.text;
            if (string.IsNullOrEmpty(current)) return;
            if (current.EndsWith(ModSuffix, StringComparison.Ordinal)) return;

            label.text = current + ModSuffix;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"LauncherVersionPatch: {ex.Message}");
        }
    }
}
