using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Api.Assets
{
    /// <summary>
    /// Sniffs a byte buffer's magic bytes, dispatches to the matching decoder
    /// (<see cref="OggLoader"/> for OGG Vorbis, <see cref="WavLoader"/> for WAV),
    /// and materializes a streaming <see cref="AudioClip"/> backed by a
    /// PCMReaderCallback that serves the decoded samples on demand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// We can't use <c>AudioClip.SetData</c> in this Il2CppInterop build: every
    /// SetData overload eventually dispatches to
    /// <c>SetData(AudioClip, ReadOnlySpan&lt;float&gt;, int)</c>, which calls
    /// <c>Il2CppSystem.ReadOnlySpan&lt;float&gt;.GetPinnableReference</c> —
    /// the same un-exported method that breaks
    /// <c>ImageConversion.LoadImage</c>. Throws <c>MissingMethodException</c>.
    /// </para>
    /// <para>
    /// Workaround: create a streaming AudioClip and feed samples through the
    /// PCMReaderCallback. The callback signature is value-type-only
    /// (<c>Il2CppStructArray&lt;float&gt;</c>), no Span involvement.
    /// </para>
    /// <para>
    /// The decoded PCM stays resident in managed memory (one <c>float[]</c>
    /// per clip). For typical CASSIE clips (~0.5-2s mono @ 48kHz) that's
    /// 100-400KB per clip; with 974 CASSIE entries the worst case if every
    /// clip were overridden would be ~300MB, but realistically only a handful
    /// are ever overridden simultaneously.
    /// </para>
    /// </remarks>
    internal static class AudioClipFactory
    {
        /// <summary>
        /// Streamers are rooted here so the GC can't collect them out from
        /// under the AudioClip's PCMReaderCallback delegate. Cleared by
        /// <see cref="ReleaseStreamer"/> when a clip is destroyed.
        /// </summary>
        private static readonly Dictionary<AudioClip, Streamer> _liveStreamers = new();

        public static AudioClip TryDecode(byte[] bytes, string clipName) =>
            TryDecode(bytes, clipName, null);

        /// <summary>
        /// Remote v1 content is OGG-only even though the local helper retains
        /// WAV support. Validate the content signature, not only the manifest
        /// filename, before entering the decoder.
        /// </summary>
        internal static AudioClip TryDecodeRemoteOgg(
            byte[] bytes,
            string clipName,
            string liveBudgetKey)
        {
            if (!HasOggHeader(bytes))
            {
                try { MelonLogger.Warning($"AudioClipFactory: remote audio for '{clipName}' was not OGG Vorbis."); }
                catch { /* format rejection must not depend on diagnostics */ }
                return null;
            }

            return TryDecode(bytes, clipName, liveBudgetKey);
        }

        internal static bool HasOggHeader(byte[] bytes) =>
            bytes != null && bytes.Length >= 4 &&
            bytes[0] == 'O' && bytes[1] == 'g' && bytes[2] == 'g' && bytes[3] == 'S';

        internal static AudioClip TryDecode(byte[] bytes, string clipName, string liveBudgetKey)
        {
            if (bytes == null || bytes.Length < 4 ||
                bytes.Length > RemoteAssetDecodeBudget.MaxEncodedAudioBytes)
                return null;

            OggLoader.PcmData pcm;
            bool ok;
            RemoteAssetDecodeBudget.Reservation reservation;

            if (HasOggHeader(bytes))
            {
                ok = OggLoader.TryDecode(bytes, out pcm, out reservation);
            }
            else if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F')
            {
                ok = WavLoader.TryDecode(bytes, out pcm, out reservation);
            }
            else
            {
                MelonLogger.Warning($"AudioClipFactory: unknown audio format for '{clipName}' (magic: {bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} {bytes[3]:X2}).");
                return null;
            }

            if (!ok || reservation == null || pcm.Samples == null || pcm.Samples.Length == 0 ||
                pcm.Channels <= 0 || pcm.Samples.Length % pcm.Channels != 0)
            {
                reservation?.Dispose();
                MelonLogger.Warning($"AudioClipFactory: decode failed for '{clipName}'.");
                return null;
            }

            for (int i = 0; i < pcm.Samples.Length; i++)
            {
                float sample = pcm.Samples[i];
                if (float.IsNaN(sample) || float.IsInfinity(sample))
                    pcm.Samples[i] = 0f;
                else if (sample > 1f)
                    pcm.Samples[i] = 1f;
                else if (sample < -1f)
                    pcm.Samples[i] = -1f;
            }

            try
            {
                int totalPerChannel = pcm.Samples.Length / pcm.Channels;
                var streamer = new Streamer(pcm.Samples, pcm.Channels, liveBudgetKey);

                var clip = AudioClip.Create(
                    name: clipName ?? "anomaly_override",
                    lengthSamples: totalPerChannel,
                    channels: pcm.Channels,
                    frequency: pcm.SampleRate,
                    stream: true,
                    pcmreadercallback: (AudioClip.PCMReaderCallback)streamer.Read,
                    pcmsetpositioncallback: (AudioClip.PCMSetPositionCallback)streamer.SetPosition);
                if (clip == null)
                {
                    reservation.Dispose();
                    return null;
                }

                _liveStreamers[clip] = streamer;
                reservation.CommitLive(liveBudgetKey);
                return clip;
            }
            catch (Exception ex)
            {
                reservation.Dispose();
                MelonLogger.Error($"AudioClipFactory.AudioClip.Create('{clipName}'): {ex}");
                return null;
            }
        }

        /// <summary>
        /// Drops the streamer rooted for <paramref name="clip"/>. Caller is
        /// responsible for actually destroying the clip; this just removes the
        /// GC root so the streamer + decoded PCM can be collected.
        /// </summary>
        public static void ReleaseStreamer(AudioClip clip)
        {
            if (ReferenceEquals(clip, null) || !_liveStreamers.TryGetValue(clip, out var streamer))
                return;
            _liveStreamers.Remove(clip);
            RemoteAssetDecodeBudget.ReleaseLive(streamer.LiveBudgetKey);
        }

        public static void ReleaseAllStreamers()
        {
            foreach (var streamer in _liveStreamers.Values)
                RemoteAssetDecodeBudget.ReleaseLive(streamer.LiveBudgetKey);
            _liveStreamers.Clear();
        }

        // -------------------------------------------------------------------

        /// <summary>
        /// Holds the decoded PCM and a playback cursor; serves samples to
        /// Unity's audio thread via the PCMReaderCallback. Position is in
        /// interleaved-sample units (i.e. one per channel-sample, not one per
        /// channel-frame), matching how the callback's data buffer is shaped.
        /// </summary>
        private sealed class Streamer
        {
            private readonly float[] _samples;
            private readonly int _channels;
            private int _pos;
            public string LiveBudgetKey { get; }

            public Streamer(float[] samples, int channels, string liveBudgetKey)
            {
                _samples = samples;
                _channels = channels;
                LiveBudgetKey = liveBudgetKey;
            }

            public void Read(Il2CppStructArray<float> data)
            {
                int dataLen = data.Length;
                int avail = _samples.Length - _pos;
                int copy = avail > dataLen ? dataLen : (avail > 0 ? avail : 0);

                for (int i = 0; i < copy; i++)
                    data[i] = _samples[_pos + i];
                for (int i = copy; i < dataLen; i++)
                    data[i] = 0f;

                _pos += copy;
            }

            public void SetPosition(int newPosition)
            {
                // Unity passes position in samples-per-channel; convert to
                // interleaved index (matching how _pos walks _samples).
                long interleaved = (long)newPosition * _channels;
                if (interleaved < 0) interleaved = 0;
                if (interleaved > _samples.Length) interleaved = _samples.Length;
                _pos = (int)interleaved;
            }
        }
    }
}
