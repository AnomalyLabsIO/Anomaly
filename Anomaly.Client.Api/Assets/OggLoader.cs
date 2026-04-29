using System;
using System.IO;
using NVorbis;

namespace Anomaly.Client.Api.Assets
{
    /// <summary>
    /// Decodes OGG Vorbis bytes into interleaved float PCM via the pure-managed
    /// <see cref="VorbisReader"/>. Caller passes the result to
    /// <see cref="AudioClipFactory"/> to materialize an <c>AudioClip</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// We don't use Unity's <c>UnityWebRequestMultimedia.GetAudioClip</c> path
    /// because it requires a real URL on disk and the Il2Cpp interop coroutine
    /// surface is awkward for synchronous use. Going pure-managed mirrors how
    /// <see cref="PngLoader"/> bypasses <c>ImageConversion.LoadImage</c>.
    /// </para>
    /// </remarks>
    internal static class OggLoader
    {
        public struct PcmData
        {
            public float[] Samples;     // interleaved if Channels > 1
            public int Channels;
            public int SampleRate;
        }

        public static bool TryDecode(byte[] bytes, out PcmData pcm)
        {
            pcm = default;
            if (bytes == null || bytes.Length < 4)
                return false;

            try
            {
                using var ms = new MemoryStream(bytes, writable: false);
                using var reader = new VorbisReader(ms, closeOnDispose: false);

                int channels = reader.Channels;
                int sampleRate = reader.SampleRate;
                long totalSamples = reader.TotalSamples; // per channel

                if (channels <= 0 || sampleRate <= 0 || totalSamples <= 0)
                    return false;

                long totalInterleaved = totalSamples * channels;
                if (totalInterleaved > int.MaxValue)
                    return false;

                var samples = new float[totalInterleaved];
                int read = 0;
                int chunk;
                while ((chunk = reader.ReadSamples(samples, read, samples.Length - read)) > 0)
                    read += chunk;

                if (read == 0)
                    return false;

                if (read < samples.Length)
                {
                    var trimmed = new float[read];
                    Array.Copy(samples, trimmed, read);
                    samples = trimmed;
                }

                pcm = new PcmData { Samples = samples, Channels = channels, SampleRate = sampleRate };
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
