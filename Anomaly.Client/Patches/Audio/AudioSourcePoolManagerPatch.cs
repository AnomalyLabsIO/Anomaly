using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using HarmonyLib;
using Il2CppAudioPooling;
using Il2CppRelativePositioning;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Patches.Audio;

/// <summary>
/// Fires <see cref="AudioEvents.Playing"/> for every clip routed through
/// <c>AudioSourcePoolManager.Play*</c>. Cancelling silences the playback
/// locally — the pooled source isn't grabbed and <c>.Play()</c> never runs.
///
/// Hot path: this fires for footsteps, gunshot tails, ambient SFX, etc. We
/// fast-path out when no subscriber is registered (the hot-path guard avoids
/// args allocation on every Play* call).
/// </summary>
[HarmonyPatch]
public static class AudioSourcePoolManagerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(AudioSourcePoolManager), nameof(AudioSourcePoolManager.Play2D))]
    public static bool Play2DPrefix(AudioClip sound, float volume, MixerChannel channel)
    {
        if (!AudioEvents.HasSubscribers) return true;
        return DispatchAndCheck(sound, position: null, channel, volume, nameof(Play2DPrefix));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AudioSourcePoolManager), nameof(AudioSourcePoolManager.Play2DWithParent))]
    public static bool Play2DWithParentPrefix(AudioClip sound, Transform parent, float volume, MixerChannel channel)
    {
        if (!AudioEvents.HasSubscribers) return true;
        return DispatchAndCheck(sound, position: null, channel, volume, nameof(Play2DWithParentPrefix));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AudioSourcePoolManager), nameof(AudioSourcePoolManager.PlayAtPosition), new System.Type[] {
        typeof(AudioClip), typeof(Vector3), typeof(float), typeof(float), typeof(Il2Cpp.FalloffType), typeof(MixerChannel), typeof(float)
    })]
    public static bool PlayAtPositionVector3Prefix(AudioClip sound, Vector3 position, float volume, MixerChannel channel)
    {
        if (!AudioEvents.HasSubscribers) return true;
        return DispatchAndCheck(sound, position, channel, volume, nameof(PlayAtPositionVector3Prefix));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AudioSourcePoolManager), nameof(AudioSourcePoolManager.PlayAtPosition), new System.Type[] {
        typeof(AudioClip), typeof(RelativePosition), typeof(float), typeof(float), typeof(Il2Cpp.FalloffType), typeof(MixerChannel), typeof(float)
    })]
    public static bool PlayAtRelativePositionPrefix(AudioClip sound, RelativePosition relativePosition, float volume, MixerChannel channel)
    {
        if (!AudioEvents.HasSubscribers) return true;
        // RelativePosition's Relative is the world-space-equivalent we surface to subscribers.
        return DispatchAndCheck(sound, relativePosition.Relative, channel, volume, nameof(PlayAtRelativePositionPrefix));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AudioSourcePoolManager), nameof(AudioSourcePoolManager.PlayOnTransform))]
    public static bool PlayOnTransformPrefix(AudioClip sound, Transform trackedTransform, float volume, MixerChannel channel)
    {
        if (!AudioEvents.HasSubscribers) return true;
        Vector3? pos = trackedTransform != null ? trackedTransform.position : (Vector3?)null;
        return DispatchAndCheck(sound, pos, channel, volume, nameof(PlayOnTransformPrefix));
    }

    private static bool DispatchAndCheck(AudioClip clip, Vector3? position, MixerChannel channel, float volume, string context)
    {
        try
        {
            AudioPlayingEventArgs ev = new(clip, position, channel, volume);
            AudioEvents.OnPlaying(ev);
            return ev.IsAllowed;
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"AudioSourcePoolManagerPatch.{context}: {ex}");
            return true;
        }
    }
}
