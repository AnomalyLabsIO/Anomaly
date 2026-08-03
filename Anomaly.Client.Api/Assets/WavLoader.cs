using System;
using System.IO;
using System.Text;

namespace Anomaly.Client.Api.Assets
{
    internal static class WavLoader
    {
        private const ushort WaveFormatPcm = 0x0001;
        private const ushort WaveFormatIeeeFloat = 0x0003;
        private const ushort WaveFormatExtensible = 0xFFFE;

        internal static bool TryDecode(
            byte[] bytes,
            out OggLoader.PcmData pcm,
            out RemoteAssetDecodeBudget.Reservation reservation)
        {
            pcm = default;
            reservation = null;
            if (bytes == null || bytes.Length < 44 ||
                bytes.Length > RemoteAssetDecodeBudget.MaxEncodedAudioBytes)
                return false;

            try
            {
                if (!TryReadMetadata(
                        bytes,
                        out ushort formatTag,
                        out ushort channels,
                        out int sampleRate,
                        out ushort bitsPerSample,
                        out int dataOffset,
                        out int dataLength,
                        out long sampleCount))
                    return false;

                long samplesPerChannel = sampleCount / channels;
                if (!OggLoader.ValidateMetadata(
                        channels,
                        sampleRate,
                        samplesPerChannel,
                        out long interleavedSamples) ||
                    interleavedSamples != sampleCount)
                    return false;

                long liveBytes = checked(sampleCount * sizeof(float));
                long peakBytes = checked(liveBytes + bytes.Length);
                if (!RemoteAssetDecodeBudget.TryReserve(
                        peakBytes,
                        liveBytes,
                        out reservation,
                        out _))
                    return false;

                float[] samples = Decode(
                    bytes,
                    dataOffset,
                    dataLength,
                    formatTag,
                    bitsPerSample,
                    checked((int)sampleCount));
                if (samples == null)
                {
                    reservation.Dispose();
                    reservation = null;
                    return false;
                }

                pcm = new OggLoader.PcmData
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

        internal static bool TryReadMetadata(
            byte[] bytes,
            out ushort formatTag,
            out ushort channels,
            out int sampleRate,
            out ushort bitsPerSample,
            out int dataOffset,
            out int dataLength,
            out long sampleCount)
        {
            formatTag = 0;
            channels = 0;
            sampleRate = 0;
            bitsPerSample = 0;
            dataOffset = 0;
            dataLength = 0;
            sampleCount = 0;

            if (bytes == null || bytes.Length < 44)
                return false;

            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
            if (ReadFourCc(reader) != "RIFF") return false;
            uint riffSize = reader.ReadUInt32();
            if (riffSize > bytes.Length - 8) return false;
            if (ReadFourCc(reader) != "WAVE") return false;

            bool foundFormat = false;
            bool foundData = false;
            while (stream.Position <= stream.Length - 8)
            {
                string chunkId = ReadFourCc(reader);
                uint rawChunkSize = reader.ReadUInt32();
                long chunkStart = stream.Position;
                long chunkEnd = checked(chunkStart + rawChunkSize);
                if (chunkEnd > stream.Length)
                    return false;

                if (chunkId == "fmt ")
                {
                    if (rawChunkSize < 16)
                        return false;
                    formatTag = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    uint rawSampleRate = reader.ReadUInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt16();
                    bitsPerSample = reader.ReadUInt16();
                    if (rawSampleRate > int.MaxValue)
                        return false;
                    sampleRate = (int)rawSampleRate;

                    if (formatTag == WaveFormatExtensible)
                    {
                        if (rawChunkSize < 40)
                            return false;
                        reader.ReadUInt16();
                        reader.ReadUInt16();
                        reader.ReadUInt32();
                        formatTag = reader.ReadUInt16();
                    }
                    foundFormat = true;
                }
                else if (chunkId == "data" && !foundData)
                {
                    if (rawChunkSize == 0 || rawChunkSize > int.MaxValue || chunkStart > int.MaxValue)
                        return false;
                    dataOffset = (int)chunkStart;
                    dataLength = (int)rawChunkSize;
                    foundData = true;
                }

                long paddedEnd = chunkEnd + (rawChunkSize & 1u);
                if (paddedEnd > stream.Length)
                    return false;
                stream.Position = paddedEnd;
            }

            if (!foundFormat || !foundData || channels == 0 || sampleRate == 0)
                return false;
            int bytesPerSample = bitsPerSample / 8;
            if ((formatTag != WaveFormatPcm && formatTag != WaveFormatIeeeFloat) ||
                (bitsPerSample != 16 && bitsPerSample != 24 && bitsPerSample != 32) ||
                (formatTag == WaveFormatIeeeFloat && bitsPerSample != 32) ||
                bytesPerSample == 0 || dataLength % bytesPerSample != 0)
                return false;

            sampleCount = dataLength / bytesPerSample;
            return sampleCount > 0 && sampleCount % channels == 0;
        }

        private static float[] Decode(
            byte[] bytes,
            int offset,
            int length,
            ushort formatTag,
            ushort bitsPerSample,
            int sampleCount)
        {
            var samples = new float[sampleCount];
            if (formatTag == WaveFormatIeeeFloat)
            {
                Buffer.BlockCopy(bytes, offset, samples, 0, length);
                for (int i = 0; i < samples.Length; i++)
                {
                    if (float.IsNaN(samples[i]) || float.IsInfinity(samples[i]))
                        samples[i] = 0f;
                }
                return samples;
            }

            int bytesPerSample = bitsPerSample / 8;
            for (int i = 0; i < sampleCount; i++)
            {
                int p = offset + (i * bytesPerSample);
                switch (bitsPerSample)
                {
                    case 16:
                        short value16 = (short)(bytes[p] | (bytes[p + 1] << 8));
                        samples[i] = value16 / 32768f;
                        break;
                    case 24:
                        int value24 = (bytes[p + 2] << 24) | (bytes[p + 1] << 16) | (bytes[p] << 8);
                        samples[i] = (value24 >> 8) / 8388608f;
                        break;
                    case 32:
                        int value32 = bytes[p] |
                                      (bytes[p + 1] << 8) |
                                      (bytes[p + 2] << 16) |
                                      (bytes[p + 3] << 24);
                        samples[i] = value32 / 2147483648f;
                        break;
                    default:
                        return null;
                }
            }
            return samples;
        }

        private static string ReadFourCc(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            return bytes.Length == 4 ? Encoding.ASCII.GetString(bytes) : string.Empty;
        }
    }
}
