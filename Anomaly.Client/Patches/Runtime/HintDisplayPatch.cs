using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using HarmonyLib;
using Il2CppHints;
using MelonLoader;

namespace Anomaly.Client.Patches.Runtime;

/// <summary>
/// Fires <see cref="HudEvents.HintDisplaying"/> when a server-sent hint is
/// about to be rendered on the local HUD. Subscribers can mutate text hints
/// and their duration. Cancelling suppresses the local render only.
/// </summary>
[HarmonyPatch]
public static class HintDisplayPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(HintDisplay), nameof(HintDisplay.ShowLocal), new[] { typeof(HintMessage) })]
    public static bool ShowLocalPrefix(HintMessage message)
    {
        try
        {
            Hint hint = message.Content;
            if (hint == null) return true;

            // TextHint contains the exact server-supplied text. Other concrete
            // types remain observable without invoking their stateful formatter.
            string text = hint is TextHint textHint ? textHint.Text : hint.ToString();
            float duration = hint.DurationScalar;

            HintDisplayingEventArgs ev = new(text, duration);
            HudEvents.OnHintDisplaying(ev);
            if (!ev.IsAllowed) return false;

            if (hint is TextHint mutableTextHint)
                mutableTextHint.Text = ev.Text ?? string.Empty;

            if (!float.IsNaN(ev.DurationSeconds) &&
                !float.IsInfinity(ev.DurationSeconds) &&
                ev.DurationSeconds >= 0f)
            {
                hint.DurationScalar = ev.DurationSeconds;
            }
            else
            {
                MelonLogger.Warning("HintDisplaying ignored a non-finite or negative duration mutation.");
            }

            return true;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"HintDisplayPatch.ShowLocalPrefix: {ex}");
            return true;
        }
    }
}
