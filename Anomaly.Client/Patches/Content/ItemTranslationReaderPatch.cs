using Anomaly.Client.Content;
using HarmonyLib;
using Il2Cpp;
using Il2CppInventorySystem.Items;

namespace Anomaly.Client.Patches.Content;

[HarmonyPatch(typeof(ItemTranslationReader), nameof(ItemTranslationReader.GetTranslation))]
internal static class ItemTranslationReaderPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemType it, ref ItemTranslationReader.NameDescriptionPair __result)
    {
        if (!ClientContentRegistrar.TryGetTranslation(it, out var custom))
            return true;
        __result = custom;
        return false;
    }
}
