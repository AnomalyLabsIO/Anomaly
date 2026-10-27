using System.Buffers.Binary;
using System.Text;
using Anomaly.Client.Scripting.Remote.Protocol;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class RemoteProtocolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fragmented_frames_preserve_values_and_clean_eof(bool fromWorker)
    {
        var value = WireValue.Object("Snapshot", new Dictionary<string, WireValue>
        {
            ["text"] = WireValue.String("snowman ☃"),
            ["values"] = WireValue.Array(new[] { WireValue.Null, WireValue.True, WireValue.Numeric(-12.5), WireValue.Reference(9, "Player", 8) })
        });
        using var stream = new FragmentedStream(Encode(value, fromWorker));
        var frame = Assert.IsType<RemoteFrame>(RemoteProtocol.Read(stream, fromWorker));
        Assert.Equal(fromWorker ? FrameKind.Completed : FrameKind.Load, frame.Kind);
        Assert.Equal(1, frame.Session);
        Assert.Equal(7, frame.Command);
        Assert.Equal("snowman ☃", frame.Data.Required("text").RequireString());
        var values = frame.Data.Required("values").Items;
        Assert.Equal(WireKind.Null, values[0].Kind);
        Assert.True(values[1].Boolean);
        Assert.Equal(-12.5, values[2].Number);
        Assert.Equal(9, values[3].Handle);
        Assert.Equal("Player", values[3].Text);
        Assert.Equal(8, values[3].Depth);
        Assert.Null(RemoteProtocol.Read(stream, fromWorker));
    }

    [Fact]
    public void Every_partial_prefix_header_and_payload_is_rejected_not_clean_eof()
    {
        var bytes = Encode(WireValue.String("payload"), true);
        for (var cut = 1; cut < bytes.Length; cut++)
        {
            using var input = new FragmentedStream(bytes[..cut]);
            Assert.Throws<EndOfStreamException>(() => RemoteProtocol.Read(input, true));
        }
        using var empty = new MemoryStream();
        Assert.Null(RemoteProtocol.Read(empty, true));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(22)]
    [InlineData(32769)]
    [InlineData(int.MaxValue)]
    public void Unchecked_outer_lengths_do_not_allocate_or_read_a_body(int length)
    {
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, length);
        using var input = new MemoryStream(prefix);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => RemoteProtocol.Read(input, true));
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 64 * 1024);
        Assert.Equal(4, input.Position);
    }

    [Fact]
    public void Host_metadata_cannot_bypass_the_smaller_worker_frame_limit()
    {
        var bytes = Encode(WireValue.String(new string('a', RemoteProtocol.MaxWorkerFrameBytes)), false);
        // Change only the direction/kind: this is a valid host-sized frame, not a valid worker-sized one.
        bytes[5] = (byte)FrameKind.Completed;
        using var input = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => RemoteProtocol.Read(input, true));
        Assert.Equal(4, input.Position);
    }

    [Theory]
    [InlineData(4, 2)] // version
    [InlineData(5, 255)] // unknown kind
    [InlineData(5, (int)FrameKind.Load)] // wrong direction
    [InlineData(6, 9)] // session above the closed range
    [InlineData(6, 255)] // negative session, completed below by mutation
    [InlineData(10, 0)] // missing command
    [InlineData(17, 128)] // negative command
    [InlineData(18, 1)] // request on completion
    [InlineData(25, 128)] // negative request
    public void Corrupt_headers_fail_before_allocating_the_advertised_body(int offset, int value)
    {
        var bytes = Encode(WireValue.Null, true)[..26];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, RemoteProtocol.MaxWorkerFrameBytes);
        bytes[offset] = (byte)value;
        if (offset == 6 && value == 255) bytes[9] = 255;
        using var input = new MemoryStream(bytes);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => RemoteProtocol.Read(input, true));
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 16 * 1024);
        Assert.Equal(26, input.Position);
    }

    [Theory]
    [InlineData((int)FrameKind.Ready, 1, 0, 0)]
    [InlineData((int)FrameKind.Ready, 0, 1, 0)]
    [InlineData((int)FrameKind.Request, 0, 1, 1)]
    [InlineData((int)FrameKind.Request, 1, 1, 0)]
    [InlineData((int)FrameKind.Configure, 1, 1, 0)]
    [InlineData((int)FrameKind.Probe, 1, 1, 0)]
    [InlineData((int)FrameKind.Load, 0, 1, 0)]
    [InlineData((int)FrameKind.Response, 1, 1, 0)]
    public void Invalid_identity_combinations_are_rejected_on_both_sides(int kindValue, int session, long command, long request)
    {
        var kind = (FrameKind)kindValue;
        bool worker = kind is FrameKind.Ready or FrameKind.Request;
        var frame = new RemoteFrame(kind, session, command, request, WireValue.Null);
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => RemoteProtocol.Write(output, frame, worker));
        Assert.Equal(0, output.Length);
        var raw = Packet(writer => writer.Write((byte)WireKind.Null), kind, session, command, request);
        using var input = new MemoryStream(raw);
        Assert.Throws<InvalidDataException>(() => RemoteProtocol.Read(input, worker));
    }

    [Fact]
    public void Each_direction_rejects_all_opposite_direction_kinds()
    {
        foreach (var kind in Enum.GetValues<FrameKind>())
        {
            bool actualWorker = (byte)kind >= 100;
            var bytes = Packet(writer => writer.Write((byte)WireKind.Null), kind, kind is FrameKind.Ready or FrameKind.Configure or FrameKind.Probe ? 0 : 1,
                kind == FrameKind.Ready ? 0 : 1, kind is FrameKind.Request or FrameKind.Response ? 1 : 0);
            using var input = new MemoryStream(bytes);
            Assert.Throws<InvalidDataException>(() => RemoteProtocol.Read(input, !actualWorker));
        }
    }

    [Fact]
    public void Worker_string_and_aggregate_byte_caps_are_enforced_before_any_output()
    {
        var oversizedString = WireValue.String(new string('a', RemoteProtocol.MaxWorkerStringBytes + 1));
        var oversizedUtf8 = WireValue.String(new string('é', RemoteProtocol.MaxWorkerStringBytes / 2 + 1));
        var oversizedFrame = WireValue.Array(Enumerable.Repeat(WireValue.String(new string('a', RemoteProtocol.MaxWorkerStringBytes)), 9).ToArray());
        foreach (var graph in new[] { oversizedString, oversizedUtf8, oversizedFrame })
        {
            using var output = new MemoryStream();
            Assert.Throws<InvalidDataException>(() => RemoteProtocol.Write(output, new RemoteFrame(FrameKind.Completed, 1, 1, 0, graph), true));
            Assert.Equal(0, output.Length);
        }
        Reject(Packet(writer =>
        {
            writer.Write((byte)WireKind.String);
            writer.Write(RemoteProtocol.MaxWorkerStringBytes + 1);
            writer.Write(new byte[RemoteProtocol.MaxWorkerStringBytes + 1]);
        }));
    }

    [Fact]
    public void Duplicate_properties_never_overwrite_a_previously_decoded_authority_field()
    {
        var bytes = Packet(writer =>
        {
            writer.Write((byte)WireKind.Object); Text(writer, ""); writer.Write(2);
            Text(writer, "target"); writer.Write((byte)WireKind.Number); writer.Write(1d);
            Text(writer, "target"); writer.Write((byte)WireKind.Number); writer.Write(2d);
        });
        Reject(bytes);
    }

    [Theory]
    [InlineData((int)WireKind.String)]
    [InlineData((int)WireKind.Array)]
    [InlineData((int)WireKind.Object)]
    public void Nested_counts_are_checked_before_collection_or_string_allocation(int kind)
    {
        foreach (int count in new[] { -1, int.MaxValue, 4097 })
        {
            var bytes = Packet(writer =>
            {
                writer.Write((byte)kind);
                if (kind == (int)WireKind.Object) Text(writer, "");
                writer.Write(count);
            });
            long before = GC.GetAllocatedBytesForCurrentThread();
            Reject(bytes);
            Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 64 * 1024);
        }
    }

    [Fact]
    public void Declared_collections_must_fit_remaining_bytes_and_remaining_nodes()
    {
        Reject(Packet(writer => { writer.Write((byte)WireKind.Array); writer.Write(200); }));
        Reject(Packet(writer => { writer.Write((byte)WireKind.Object); Text(writer, ""); writer.Write(200); }));
        Reject(Packet(writer => { writer.Write((byte)WireKind.Array); writer.Write(256); writer.Write(new byte[256]); }));
        var graph = WireValue.Array(Enumerable.Repeat(WireValue.Null, 256).ToArray());
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => RemoteProtocol.Write(output, new RemoteFrame(FrameKind.Completed, 1, 7, 0, graph), true));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Excessive_depth_and_cyclic_writer_graphs_are_rejected_before_output()
    {
        Reject(Packet(writer =>
        {
            for (var depth = 0; depth < 17; depth++) { writer.Write((byte)WireKind.Array); writer.Write(1); }
            writer.Write((byte)WireKind.Null);
        }));
        var items = new List<WireValue>();
        var cycle = WireValue.Array(items);
        items.Add(cycle);
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => RemoteProtocol.Write(output, new RemoteFrame(FrameKind.Load, 1, 7, 0, cycle)));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("boolean")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("kind")]
    [InlineData("trailing")]
    [InlineData("reference handle")]
    [InlineData("reference depth")]
    [InlineData("reference alias")]
    public void Invalid_scalar_encodings_do_not_cross_the_protocol(string corruption)
    {
        var bytes = Packet(writer =>
        {
            switch (corruption)
            {
                case "utf8": writer.Write((byte)WireKind.String); writer.Write(2); writer.Write(new byte[] { 0xc0, 0xaf }); break;
                case "boolean": writer.Write((byte)WireKind.Boolean); writer.Write((byte)2); break;
                case "nan": writer.Write((byte)WireKind.Number); writer.Write(double.NaN); break;
                case "infinity": writer.Write((byte)WireKind.Number); writer.Write(double.PositiveInfinity); break;
                case "kind": writer.Write((byte)255); break;
                case "trailing": writer.Write((byte)WireKind.Null); writer.Write((byte)WireKind.Null); break;
                default:
                    writer.Write((byte)WireKind.Reference);
                    Text(writer, corruption == "reference alias" ? "" : "Player");
                    writer.Write(corruption == "reference handle" ? 0L : 3L);
                    writer.Write(corruption == "reference depth" ? 9 : 1);
                    break;
            }
        });
        using var input = new MemoryStream(bytes);
        var error = Record.Exception(() => RemoteProtocol.Read(input, true));
        Assert.True(error is InvalidDataException or DecoderFallbackException, error?.ToString() ?? "Malformed scalar was accepted.");
    }

    private static void Reject(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => RemoteProtocol.Read(input, true));
    }

    private static byte[] Encode(WireValue value, bool worker)
    {
        using var output = new MemoryStream();
        RemoteProtocol.Write(output, new RemoteFrame(worker ? FrameKind.Completed : FrameKind.Load, 1, 7, 0, value), worker);
        return output.ToArray();
    }

    // Deliberately independent encoder: malformed data must not be sanitized by the production writer.
    private static byte[] Packet(Action<BinaryWriter> body, FrameKind kind = FrameKind.Completed, int session = 1, long command = 7, long request = 0)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(0); writer.Write((byte)1); writer.Write((byte)kind);
        writer.Write(session); writer.Write(command); writer.Write(request); body(writer);
        var bytes = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes, bytes.Length - 4);
        return bytes;
    }

    private static void Text(BinaryWriter writer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        writer.Write(bytes.Length); writer.Write(bytes);
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(1, buffer.Length)]);
    }
}
