using System;
using System.IO;
using System.Text;

namespace Anomaly.Client.Api.Assets
{
    /// <summary>
    /// Decodes uncompressed RIFF/WAV bytes into interleaved float PCM. Supports
    /// PCM int16 / int24 / int32 and IEEE float32. Compressed formats (ADPCM,
    /// A-law, mu-law) are rejected up front.
    /// </summary>
    internal static class WavLoader
    {
        private const ushort WaveFormatPcm = 0x0001;
        private const ushort WaveFormatIeeeFloat = 0x0003;
        private const ushort WaveFormatExtensible = 0xFFFE;

        public static bool TryDecode(byte[] bytes, out OggLoader.PcmData pcm)
        {
            pcm = default;
            if (bytes == null || bytes.Length < 44)
                return false;

            try
            {
                using var ms = new MemoryStream(bytes, writable: false);
                using var br = new BinaryReader(ms, Encoding.ASCII, leaveOpen: false);

                if (ReadFourCc(br) != "RIFF") return false;
                br.ReadUInt32();                    // riff size
                if (ReadFourCc(br) != "WAVE") return false;

                ushort formatTag = 0;
                ushort channels = 0;
                uint sampleRate = 0;
                ushort bitsPerSample = 0;
                byte[] data = null;

                while (ms.Position < ms.Length - 8)
                {
                    string chunkId = ReadFourCc(br);
                    uint chunkSize = br.ReadUInt32();
                    long chunkEnd = ms.Position + chunkSize;

                    if (chunkId == "fmt ")
                    {
                        formatTag = br.ReadUInt16();
                        channels = br.ReadUInt16();
                        sampleRate = br.ReadUInt32();
                        br.ReadUInt32();            // byte rate
                        br.ReadUInt16();            // block align
                        bitsPerSample = br.ReadUInt16();

                        if (formatTag == WaveFormatExtensible && chunkSize >= 40)
                        {
                            br.ReadUInt16();        // cb size
                            br.ReadUInt16();        // valid bits per sample
                            br.ReadUInt32();        // channel mask
                            // First two bytes of the SubFormat GUID are the actual format tag.
                            formatTag = br.ReadUInt16();
                        }
                    }
                    else if (chunkId == "data")
                    {
                        data = br.ReadBytes((int)chunkSize);
                    }

                    // Pad to even boundary (RIFF chunks are word-aligned).
                    if ((chunkEnd & 1) == 1) chunkEnd++;
                    ms.Position = chunkEnd;
                }

                if (channels == 0 || sampleRate == 0 || data == null || data.Length == 0)
                    return false;

                float[] samples = formatTag switch
                {
                    WaveFormatPcm => bitsPerSample switch
                    {
                        16 => DecodePcm16(data),
                        24 => DecodePcm24(data),
                        32 => DecodePcm32(data),
                        _ => null,
                    },
                    WaveFormatIeeeFloat => bitsPerSample == 32 ? DecodeFloat32(data) : null,
                    _ => null,
                };

                if (samples == null)
                    return false;

                pcm = new OggLoader.PcmData
                {
                    Samples = samples,
                    Channels = channels,
                    SampleRate = (int)sampleRate,
                };
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ReadFourCc(BinaryReader br) => Encoding.ASCII.GetString(br.ReadBytes(4));

        private static float[] DecodePcm16(byte[] data)
        {
            int n = data.Length / 2;
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                short v = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
                s[i] = v / 32768f;
            }
            return s;
        }

        private static float[] DecodePcm24(byte[] data)
        {
            int n = data.Length / 3;
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                int b0 = data[i * 3];
                int b1 = data[i * 3 + 1];
                int b2 = data[i * 3 + 2];
                int v = (b2 << 24) | (b1 << 16) | (b0 << 8);
                v >>= 8;                            // sign-extend 24 -> 32
                s[i] = v / 8388608f;                // 2^23
            }
            return s;
        }

        private static float[] DecodePcm32(byte[] data)
        {
            int n = data.Length / 4;
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                int v = data[i * 4]
                    | (data[i * 4 + 1] << 8)
                    | (data[i * 4 + 2] << 16)
                    | (data[i * 4 + 3] << 24);
                s[i] = v / 2147483648f;             // 2^31
            }
            return s;
        }

        private static float[] DecodeFloat32(byte[] data)
        {
            int n = data.Length / 4;
            var s = new float[n];
            Buffer.BlockCopy(data, 0, s, 0, n * 4);
            return s;
        }
    }
}
