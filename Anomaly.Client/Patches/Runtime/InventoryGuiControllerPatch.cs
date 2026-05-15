using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using HarmonyLib;
using Il2CppInventorySystem.GUI;
using Il2CppToggleableMenus;
using MelonLoader;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Fires <see cref="InventoryEvents.UiOpening"/> / <see cref="InventoryEvents.UiClosing"/>
/// when <c>InventoryGuiController.IsEnabled</c> is about to transition. Since
/// <c>IsEnabled</c> is defined on the base <c>ToggleableMenuBase</c>, we patch
/// there and filter to the inventory-controller instance.
///
/// The setter mutates only a local bool and calls <c>OnToggled</c>; no network
/// state is touched, so cancelling is a pure local-UI suppression.
/// </summary>
[HarmonyPatch]
public static class InventoryGuiControllerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ToggleableMenuBase), nameof(ToggleableMenuBase.IsEnabled), MethodType.Setter)]
    public static bool SetIsEnabledPrefix(ToggleableMenuBase __instance, bool value)
    {
        try
        {
            InventoryGuiController gui = __instance.TryCast<InventoryGuiController>();
            if (gui == null) return true; // not the inventory UI; let other ToggleableMenu users through

            bool current = gui.IsEnabled;
            if (current == value) return true; // no transition

            if (value)
            {
                InventoryUiOpeningEventArgs ev = new();
                InventoryEvents.OnUiOpening(ev);
                return ev.IsAllowed;
            }
            else
            {
                InventoryUiClosingEventArgs ev = new();
                InventoryEvents.OnUiClosing(ev);
                return ev.IsAllowed;
            }
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"InventoryGuiControllerPatch.SetIsEnabledPrefix: {ex}");
            return true;
        }
    }
}
