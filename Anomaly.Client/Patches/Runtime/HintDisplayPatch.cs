using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using HarmonyLib;
using Il2CppHints;
using MelonLoader;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Fires <see cref="HudEvents.HintDisplaying"/> when a server-sent hint is
/// about to be rendered on the local HUD. Cancelling suppresses the local
/// render only.
///
/// TODO: the right client-side hook hasn't been pinpointed yet. <c>HintDisplay.Show</c>
/// is server-only (it throws on isLocalPlayer). The actual client renderer is
/// likely a <c>HintCanvas</c>/<c>HintBubble</c> component that subscribes to
/// incoming <c>HintMessage</c> Mirror messages — once that is located, replace
/// the <c>[HarmonyPatch(...)]</c> target below. For now this is wired against
/// <c>HintDisplay.Show</c> so the file compiles; it will not fire on a normal
/// client until the patch target is corrected.
/// </summary>
[HarmonyPatch]
public static class HintDisplayPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(HintDisplay), nameof(HintDisplay.Show))]
    public static bool ShowPrefix(Hint hint)
    {
        try
        {
            if (hint == null) return true;

            // Best-effort text extraction. Concrete Hint subclasses (TextHint, TranslationHint)
            // expose the actual string; we surface .ToString() as a fallback.
            string text = hint.ToString();
            float duration = 0f;

            HintDisplayingEventArgs ev = new(text, duration);
            HudEvents.OnHintDisplaying(ev);
            return ev.IsAllowed;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"HintDisplayPatch.ShowPrefix: {ex}");
            return true;
        }
    }
}
