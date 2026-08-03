using System;
using System.IO;
using NVorbis;

namespace Anomaly.Client.Api.Assets
{
    internal static class OggLoader
    {
        public struct PcmData
        {
            public float[] Samples;
            public int Channels;
            public int SampleRate;
        }

        internal static bool TryDecode(
            byte[] bytes,
            out PcmData pcm,
            out RemoteAssetDecodeBudget.Reservation reservation)
        {
            pcm = default;
            reservation = null;
            if (bytes == null || bytes.Length < 4 ||
                bytes.Length > RemoteAssetDecodeBudget.MaxEncodedAudioBytes)
                return false;

            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var reader = new VorbisReader(stream, closeOnDispose: false);

                int channels = reader.Channels;
                int sampleRate = reader.SampleRate;
                long samplesPerChannel = reader.TotalSamples;
                if (!ValidateMetadata(channels, sampleRate, samplesPerChannel, out long interleavedSamples))
                    return false;

                long liveBytes = checked(interleavedSamples * sizeof(float));
                long peakBytes = checked(liveBytes + bytes.Length + (4L * 1024L * 1024L));
                if (!RemoteAssetDecodeBudget.TryReserve(
                        peakBytes,
                        liveBytes,
                        out reservation,
                        out _))
                    return false;

                var samples = new float[checked((int)interleavedSamples)];
                int read = 0;
                int chunk;
                while (read < samples.Length &&
                       (chunk = reader.ReadSamples(samples, read, samples.Length - read)) > 0)
                    read += chunk;

                if (read <= 0)
                {
                    reservation.Dispose();
                    reservation = null;
                    return false;
                }

                pcm = new PcmData
                {
                    Samples = samples,
                    Channels = channels,
                    SampleRate = sampleRate
                };
                return true;
            }
            catch (Exception)
            {
                reservation?.Dispose();
                reservation = null;
                pcm = default;
                return false;
            }
        }

        internal static bool ValidateMetadata(
            int channels,
            int sampleRate,
            long samplesPerChannel,
            out long interleavedSamples)
        {
            interleavedSamples = 0;
            if (channels <= 0 || channels > RemoteAssetDecodeBudget.MaxAudioChannels ||
                sampleRate < RemoteAssetDecodeBudget.MinAudioSampleRate ||
                sampleRate > RemoteAssetDecodeBudget.MaxAudioSampleRate ||
                samplesPerChannel <= 0 ||
                (double)samplesPerChannel / sampleRate > RemoteAssetDecodeBudget.MaxAudioDurationSeconds)
                return false;

            try
            {
                interleavedSamples = checked(samplesPerChannel * channels);
                return checked(interleavedSamples * sizeof(float)) <=
                       RemoteAssetDecodeBudget.MaxDecodedBytesPerAsset;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }
}
