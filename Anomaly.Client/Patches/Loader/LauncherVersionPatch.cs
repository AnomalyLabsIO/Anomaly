using System;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace Anomaly.Client.Patches.Loader;

/// <summary>
/// Appends the shared version-modded suffix (translation key
/// <c>anomaly.version_modded_suffix</c>) to the PreLoader scene's version label,
/// e.g. "RELEASE 14.2.6" → "RELEASE 14.2.6 (MODDED)". The same key is used by
/// <c>MainMenuCustomizer</c> so both labels stay in sync.
/// </summary>
[HarmonyPatch(typeof(LauncherAssetScanProgressBar), "Awake")]
public static class LauncherVersionPatch
{
    [HarmonyPostfix]
    public static void Postfix(LauncherAssetScanProgressBar __instance)
    {
        try
        {
            var label = __instance._version;
            if (label == null) return;

            string current = label.text;
            if (string.IsNullOrEmpty(current)) return;

            string suffix = Tr.Get(TrKeys.Version_ModSuffix);
            if (current.EndsWith(suffix, StringComparison.Ordinal)) return;

            label.text = "<color=\"red\">"+current + suffix;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"LauncherVersionPatch: {ex.Message}");
        }
    }
}
