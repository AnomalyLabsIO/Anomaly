using System.Reflection;
using Anomaly.Client.Api.Assets;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class DecodeBudgetTests
{
    private static readonly Assembly ApiAssembly = typeof(AnomalyResources).Assembly;

    [Fact]
    public void Png_header_parser_uses_checked_big_endian_dimensions()
    {
        byte[] header = PngHeader(4096, 2048);
        object?[] args = { header, 0, 0 };

        bool accepted = (bool)Invoke("Anomaly.Client.Api.Assets.PngLoader", "TryReadDimensions", args)!;

        Assert.True(accepted);
        Assert.Equal(4096, args[1]);
        Assert.Equal(2048, args[2]);

        header[16] = header[17] = header[18] = header[19] = 0;
        args = new object?[] { header, 0, 0 };
        Assert.False((bool)Invoke("Anomaly.Client.Api.Assets.PngLoader", "TryReadDimensions", args)!);
    }

    [Theory]
    [InlineData(1, 48000, 48000, true)]
    [InlineData(2, 48000, 1440000, true)] // 30 seconds
    [InlineData(3, 48000, 48000, false)]
    [InlineData(2, 192000, 192000, false)]
    [InlineData(1, 48000, 1488000, false)] // 31 seconds
    public void Audio_metadata_budget_rejects_unsafe_expansion(
        int channels,
        int sampleRate,
        long samplesPerChannel,
        bool expected)
    {
        object?[] args = { channels, sampleRate, samplesPerChannel, 0L };

        bool accepted = (bool)Invoke("Anomaly.Client.Api.Assets.OggLoader", "ValidateMetadata", args)!;

        Assert.Equal(expected, accepted);
        if (accepted)
            Assert.Equal(samplesPerChannel * channels, args[3]);
    }

    [Fact]
    public void Wav_parser_bounds_chunk_lengths_and_sample_count()
    {
        byte[] valid = Wav(seconds: 1, channels: 1, sampleRate: 8000, bitsPerSample: 16);
        object?[] args = { valid, (ushort)0, (ushort)0, 0, (ushort)0, 0, 0, 0L };

        bool accepted = (bool)Invoke("Anomaly.Client.Api.Assets.WavLoader", "TryReadMetadata", args)!;

        Assert.True(accepted);
        Assert.Equal(8000L, args[7]);

        byte[] truncated = (byte[])valid.Clone();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(truncated, 40);
        args = new object?[] { truncated, (ushort)0, (ushort)0, 0, (ushort)0, 0, 0, 0L };
        Assert.False((bool)Invoke("Anomaly.Client.Api.Assets.WavLoader", "TryReadMetadata", args)!);
    }

    [Fact]
    public void Aggregate_budget_rejects_oversized_single_asset()
    {
        object?[] args = { 70L * 1024 * 1024, 65L * 1024 * 1024, null, null };

        bool accepted = (bool)Invoke("Anomaly.Client.Api.Assets.RemoteAssetDecodeBudget", "TryReserve", args)!;

        Assert.False(accepted);
        Assert.Null(args[2]);
    }

    private static object? Invoke(string typeName, string methodName, object?[] args)
    {
        Type type = ApiAssembly.GetType(typeName, throwOnError: true)!;
        MethodInfo method = type.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        return method.Invoke(null, args);
    }

    private static byte[] PngHeader(uint width, uint height)
    {
        var bytes = new byte[24];
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        signature.CopyTo(bytes, 0);
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static void WriteBigEndian(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    private static byte[] Wav(int seconds, ushort channels, int sampleRate, ushort bitsPerSample)
    {
        int bytesPerSample = bitsPerSample / 8;
        int dataLength = checked(seconds * sampleRate * channels * bytesPerSample);
        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        writer.Write((uint)(36 + dataLength));
        writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
        writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        writer.Write(16u);
        writer.Write((ushort)1);
        writer.Write(channels);
        writer.Write((uint)sampleRate);
        writer.Write((uint)(sampleRate * channels * bytesPerSample));
        writer.Write((ushort)(channels * bytesPerSample));
        writer.Write(bitsPerSample);
        writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        writer.Write((uint)dataLength);
        writer.Write(new byte[dataLength]);
        return stream.ToArray();
    }
}
