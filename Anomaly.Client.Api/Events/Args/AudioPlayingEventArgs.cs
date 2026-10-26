using Il2CppAudioPooling;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when <c>AudioSourcePoolManager</c> is about to play a sound on this
/// client. Cancelling silences the playback locally; nothing about the server
/// or other clients changes. Subscribers should filter by <see cref="Clip"/>
/// identity early to avoid per-frame overhead — this fires for footsteps,
/// gunshot tails, ambient SFX, etc.
/// </summary>
[ScriptType]
public class AudioPlayingEventArgs : EventArgs, ICancellableEvent
{
    public AudioPlayingEventArgs(AudioClip clip, Vector3? position, MixerChannel channel, float volume)
    {
        Clip = clip;
        Position = position;
        Channel = channel;
        Volume = volume;
    }

    /// <summary>The audio clip about to play. Use this for cheap identity-based filtering.</summary>
    public AudioClip Clip { get; }

    /// <summary>The local clip name, or an empty string when no clip is available.</summary>
    [ScriptVisible]
    public string ClipName => Clip != null ? Clip.name : string.Empty;

    /// <summary>World-space position for 3D variants. <c>null</c> for the 2D overloads (Play2D, Play2DWithParent).</summary>
    public Vector3? Position { get; }

    /// <summary>Which mixer channel the sound was going to play on (DefaultSfx, Voice, etc.).</summary>
    [ScriptVisible]
    public MixerChannel Channel { get; }

    /// <summary>Volume scalar in [0, 1].</summary>
    [ScriptVisible]
    public float Volume { get; }

    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
