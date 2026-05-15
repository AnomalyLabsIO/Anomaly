using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// Local audio playback events. Fires for every clip routed through
/// <c>AudioSourcePoolManager</c> — including footsteps, gunshot tails, item
/// sounds, ambient SFX. Cancellation silences playback locally with no
/// network implications. Subscribers should filter by clip identity early
/// (e.g., <c>args.Clip == myMutedClip</c>) to keep per-frame cost minimal.
/// </summary>
public static class AudioEvents
{
    /// <summary>Fires before a pooled audio source plays on this client. Cancel to silence.</summary>
    public static event AnomalyEventHandler<AudioPlayingEventArgs> Playing;

    internal static void OnPlaying(AudioPlayingEventArgs args) => EventInvoker.Invoke(Playing, args);

    /// <summary>True when at least one subscriber is registered. Patches can early-out using this to avoid args allocation on hot paths.</summary>
    internal static bool HasSubscribers => Playing != null;
}
