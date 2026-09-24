using Anomaly.Client.Api.Assets;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class Rgba32ResourceTests
{
    [Fact]
    public void PreservesBottomUpPixelOrderAndAlpha()
    {
        byte[] pixels = { 0, 0, 255, 128, 255, 255, 255, 0, 255, 0, 0, 255, 0, 255, 0, 64 };
        using var stream = Make(2, 2, pixels);
        Assert.Equal(pixels, Rgba32Resource.Read(stream, out int width, out int height));
        Assert.Equal(2, width);
        Assert.Equal(2, height);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(4097, 1, 0)]
    [InlineData(int.MaxValue, int.MaxValue, 0)]
    [InlineData(1, 1, 3)]
    [InlineData(1, 1, 5)]
    public void RejectsDimensionsAndInexactPayloadBeforePixelAllocation(int width, int height, int bytes)
    {
        using var stream = Make(width, height, new byte[bytes]);
        Assert.Throws<InvalidDataException>(() => Rgba32Resource.Read(stream, out _, out _));
    }

    private static MemoryStream Make(int width, int height, byte[] pixels)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("ARGBA32\0"));
            writer.Write(width);
            writer.Write(height);
            writer.Write(pixels);
        }
        stream.Position = 0;
        return stream;
    }
}
