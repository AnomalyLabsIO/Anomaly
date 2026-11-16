using System.Text;
using Anomaly.Client.Api.Internal;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class StockDisconnectReasonTests
{
    [Theory]
    [InlineData("Content transfer failed: available upload capacity cannot complete this request within the content admission deadline")]
    [InlineData("Сервер завершил подключение.")]
    [InlineData("Server message: <color=red>literal text</color>\nSecond line")]
    public void Existing_stock_string_and_base64_format_roundtrips_exact_text(string expected)
    {
        Assert.True(StockDisconnectReason.TryDecode(Frame(expected), out var reason));
        Assert.Equal(expected, reason);
    }

    [Fact]
    public void Maximum_bounded_frame_is_accepted_and_larger_frame_is_rejected()
    {
        string expected = new('x', 765);
        byte[] bounded = Frame(expected);
        Assert.Equal(1022, bounded.Length);
        Assert.True(StockDisconnectReason.TryDecode(bounded, out var reason));
        Assert.Equal(expected, reason);
        Assert.False(StockDisconnectReason.TryDecode(Frame(new string('x', 766)), out _));
    }

    [Fact]
    public void Truncated_extra_empty_and_inconsistent_string_frames_are_rejected()
    {
        var valid = Frame("Server closed.");
        Assert.False(StockDisconnectReason.TryDecode(null!, out _));
        Assert.False(StockDisconnectReason.TryDecode(Array.Empty<byte>(), out _));
        Assert.False(StockDisconnectReason.TryDecode(new byte[] { 0, 0 }, out _));
        Assert.False(StockDisconnectReason.TryDecode(new byte[] { 1, 0 }, out _));
        Assert.False(StockDisconnectReason.TryDecode(valid[..^1], out _));
        Assert.False(StockDisconnectReason.TryDecode(valid.Concat(new byte[] { 0 }).ToArray(), out _));
        valid[0]--;
        Assert.False(StockDisconnectReason.TryDecode(valid, out _));
    }

    [Theory]
    [InlineData("%%%%")]
    [InlineData("YQ=\n")]
    [InlineData("YQ= ")]
    [InlineData("YQ==YQ==")]
    [InlineData("YR==")]
    [InlineData("YQ")]
    [InlineData("/w==")]
    [InlineData("wIA=")]
    [InlineData("ICAg")]
    public void Malformed_noncanonical_base64_invalid_UTF8_and_blank_reasons_are_rejected(string encoded)
    {
        Assert.False(StockDisconnectReason.TryDecode(FrameEncoded(encoded), out _));
    }

    [Fact]
    public void Parsing_does_not_mutate_the_detached_reader_bytes()
    {
        byte[] frame = Frame("The server ended this connection.");
        byte[] before = (byte[])frame.Clone();
        Assert.True(StockDisconnectReason.TryDecode(frame, out _));
        Assert.Equal(before, frame);
    }

    internal static byte[] Frame(string reason) => FrameEncoded(Convert.ToBase64String(Encoding.UTF8.GetBytes(reason)));

    private static byte[] FrameEncoded(string encoded)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(encoded);
        byte[] result = new byte[bytes.Length + 2];
        ushort storedLength = checked((ushort)(bytes.Length + 1));
        result[0] = (byte)storedLength;
        result[1] = (byte)(storedLength >> 8);
        bytes.CopyTo(result, 2);
        return result;
    }
}
