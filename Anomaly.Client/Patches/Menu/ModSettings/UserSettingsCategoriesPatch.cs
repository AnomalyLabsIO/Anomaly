using HarmonyLib;
using Il2CppUserSettings.GUIElements;

namespace Anomaly.Client.Patches.Menu.ModSettings;

/// <summary>
/// Hooks <see cref="UserSettingsCategories.Awake"/> on both the main-menu and
/// in-game settings panels to inject the "Mods" tab. The prefix expands the
/// <c>_categories</c> array (so the stock <c>Awake</c> loop auto-wires our new
/// tab's <c>onClick</c>) and the postfix populates the new tab's content with
/// widgets bound to MelonPreferences entries.
/// </summary>
[HarmonyPatch(typeof(UserSettingsCategories), nameof(UserSettingsCategories.Awake))]
internal static class UserSettingsCategoriesPatch
{
    [HarmonyPrefix]
    public static void Prefix(UserSettingsCategories __instance)
        => ModSettingsTabInstaller.InstallIfNeeded(__instance);

    [HarmonyPostfix]
    public static void Postfix(UserSettingsCategories __instance)
        => ModSettingsTabInstaller.PopulateNewlyInstalled(__instance);
}
