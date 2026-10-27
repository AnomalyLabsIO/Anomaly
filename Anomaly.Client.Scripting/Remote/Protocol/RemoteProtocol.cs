using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Anomaly.Client.Scripting.Remote.Protocol;

internal enum WireKind : byte { Null, Boolean, Number, String, Object, Array, Reference }
internal enum FrameKind : byte {
    Configure = 1, Load = 2, Invoke = 3, InvokeFunction = 4, Response = 5, Probe = 6,
    Ready = 101, Request = 102, Completed = 103, Fault = 104
}

internal sealed class WireValue {
    private static readonly IReadOnlyDictionary<string, WireValue> EmptyFields = new Dictionary<string, WireValue>();
    private static readonly IReadOnlyList<WireValue> EmptyItems = System.Array.Empty<WireValue>();
    private readonly IReadOnlyDictionary<string, WireValue>? _fields;
    private readonly IReadOnlyList<WireValue>? _items;
    private WireValue(WireKind kind, bool boolean = false, double number = 0, string? text = null,
        IReadOnlyDictionary<string, WireValue>? fields = null, IReadOnlyList<WireValue>? items = null, long handle = 0, int depth = 0) {
        Kind = kind; Boolean = boolean; Number = number; Text = text ?? "";
        _fields = fields; _items = items; Handle = handle; Depth = depth;
    }
    public WireKind Kind { get; }
    public bool Boolean { get; }
    public double Number { get; }
    public string Text { get; }
    public IReadOnlyDictionary<string, WireValue> Fields => _fields ?? EmptyFields;
    public IReadOnlyList<WireValue> Items => _items ?? EmptyItems;
    public long Handle { get; }
    public int Depth { get; }
    public static WireValue Null { get; } = new(WireKind.Null);
    public static WireValue True { get; } = new(WireKind.Boolean, boolean: true);
    public static WireValue False { get; } = new(WireKind.Boolean);
    public static WireValue Flag(bool value) => value ? True : False;
    public static WireValue Numeric(double value) => double.IsFinite(value) ? new(WireKind.Number, number: value) : throw new InvalidDataException("Non-finite wire number.");
    public static WireValue String(string value) => new(WireKind.String, text: value ?? throw new ArgumentNullException(nameof(value)));
    public static WireValue Object(string typeName, IReadOnlyDictionary<string, WireValue> fields) => new(WireKind.Object, text: typeName, fields: fields ?? throw new ArgumentNullException(nameof(fields)));
    public static WireValue Array(IReadOnlyList<WireValue> items) => new(WireKind.Array, items: items ?? throw new ArgumentNullException(nameof(items)));
    public static WireValue Reference(long handle, string typeName, int depth) => handle > 0 && depth is >= 0 and <= 8 && !string.IsNullOrEmpty(typeName)
        ? new(WireKind.Reference, text: typeName, handle: handle, depth: depth) : throw new InvalidDataException("Invalid wire reference.");
    public WireValue Required(string name) => Kind == WireKind.Object && Fields.TryGetValue(name, out var value) ? value : throw new InvalidDataException("Missing wire field: " + name);
    public WireValue Optional(string name) => Kind == WireKind.Object && Fields.TryGetValue(name, out var value) ? value : Null;
    public string RequireString(int maximum = 4096) => Kind == WireKind.String && Text.Length <= maximum && RemoteProtocol.Utf8.GetByteCount(Text) <= maximum
        ? Text : throw new InvalidDataException("Invalid wire string.");
    public long RequireInteger(long minimum, long maximum) => Kind == WireKind.Number && Number >= minimum && Number <= maximum && Math.Abs(Number) <= 9007199254740991d && Math.Truncate(Number) == Number
        ? (long)Number : throw new InvalidDataException("Invalid wire integer.");
    public bool RequireBoolean() => Kind == WireKind.Boolean ? Boolean : throw new InvalidDataException("Invalid wire Boolean.");
    public IReadOnlyList<WireValue> RequireArray(int maximum) => Kind == WireKind.Array && Items.Count <= maximum ? Items : throw new InvalidDataException("Invalid wire array.");
    public IReadOnlyDictionary<string, WireValue> RequireObject(int maximum) => Kind == WireKind.Object && Fields.Count <= maximum ? Fields : throw new InvalidDataException("Invalid wire record.");
}

internal sealed record RemoteFrame(FrameKind Kind, int Session, long Command, long Request, WireValue Data);

/// <summary>Closed, length-bounded IPC; no allocation depends on an unchecked peer length.</summary>
internal static class RemoteProtocol {
    public const int Version = 1;
    public const int MaxFrameBytes = 512 * 1024;
    public const int MaxWorkerFrameBytes = 32 * 1024;
    public const int MaxWorkerNodes = 256;
    public const int MaxWorkerStringBytes = 4096;
    public const int MaxMetadataNodes = 16384;
    public const int MaxMetadataStringBytes = 65536;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    private const int HeaderBytes = 22;
    private const int MaximumDepth = 16;

    public static void Write(Stream stream, RemoteFrame frame, bool fromWorker = false) {
        ValidateHeader(frame.Kind, frame.Session, frame.Command, frame.Request, fromWorker);
        var limits = new Limits(fromWorker);
        int length = HeaderBytes;
        Measure(frame.Data, 0, limits, ref length);
        if (length > limits.Bytes) throw new InvalidDataException("Wire frame byte limit exceeded.");
        // Allocate only after the complete graph has passed the bounded sizing walk.
        var bytes = new byte[length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), length);
        bytes[4] = Version;
        bytes[5] = (byte)frame.Kind;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(6, 4), frame.Session);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(10, 8), frame.Command);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(18, 8), frame.Request);
        int offset = 26;
        Encode(frame.Data, bytes, ref offset);
        if (offset != bytes.Length) throw new InvalidDataException("Wire graph changed while encoding.");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    public static RemoteFrame? Read(Stream stream, bool fromWorker = false) {
        Span<byte> prefix = stackalloc byte[4];
        int first = stream.ReadByte();
        if (first < 0) return null;
        prefix[0] = (byte)first;
        ReadExactly(stream, prefix.Slice(1));
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        var limits = new Limits(fromWorker);
        if (length < HeaderBytes + 1 || length > limits.Bytes) throw new InvalidDataException("Invalid wire frame length.");
        // Read and validate the fixed header before allocating/decoding peer graphs.
        Span<byte> header = stackalloc byte[HeaderBytes];
        ReadExactly(stream, header);
        if (header[0] != Version) throw new InvalidDataException("Unsupported worker protocol.");
        var kind = (FrameKind)header[1];
        int session = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(2, 4));
        long command = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(6, 8));
        long request = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(14, 8));
        ValidateHeader(kind, session, command, request, fromWorker);
        var body = new byte[length - HeaderBytes];
        ReadExactly(stream, body);
        int offset = 0;
        var data = Decode(body, ref offset, 0, limits);
        if (offset != body.Length) throw new InvalidDataException("Trailing worker frame data.");
        return new RemoteFrame(kind, session, command, request, data);
    }

    private static void ValidateHeader(FrameKind kind, int session, long command, long request, bool fromWorker) {
        bool permitted = fromWorker
            ? kind is FrameKind.Ready or FrameKind.Request or FrameKind.Completed or FrameKind.Fault
            : kind is FrameKind.Configure or FrameKind.Load or FrameKind.Invoke or FrameKind.InvokeFunction or FrameKind.Response or FrameKind.Probe;
        if (!permitted || session is < 0 or > 8 || command < 0 || request < 0) throw new InvalidDataException("Invalid worker frame header.");
        if (kind == FrameKind.Ready) {
            if (session != 0 || command != 0 || request != 0) throw new InvalidDataException("Invalid worker greeting.");
        } else {
            if (command == 0) throw new InvalidDataException("Missing worker command identity.");
            if ((kind is FrameKind.Request or FrameKind.Response) != (request > 0)) throw new InvalidDataException("Invalid worker request identity.");
            if (kind is FrameKind.Configure or FrameKind.Probe && session != 0) throw new InvalidDataException("Invalid worker setup session.");
            if (kind is FrameKind.Load or FrameKind.Invoke or FrameKind.InvokeFunction or FrameKind.Request && session == 0) throw new InvalidDataException("Missing worker session.");
        }
    }

    private sealed class Limits {
        public readonly int Bytes, Nodes, StringBytes, Collection;
        public int UsedNodes;
        public Limits(bool fromWorker) {
            Bytes = fromWorker ? MaxWorkerFrameBytes : MaxFrameBytes;
            Nodes = fromWorker ? MaxWorkerNodes : MaxMetadataNodes;
            StringBytes = fromWorker ? MaxWorkerStringBytes : MaxMetadataStringBytes;
            Collection = fromWorker ? 256 : 4096;
        }
        public void Node(int depth) {
            if (depth > MaximumDepth || ++UsedNodes > Nodes) throw new InvalidDataException("Wire graph limit exceeded.");
        }
    }

    private static void Add(ref int length, int amount, Limits limits) {
        if (amount < 0 || amount > limits.Bytes - length) throw new InvalidDataException("Wire frame byte limit exceeded.");
        length += amount;
    }
    private static void MeasureText(string text, int maximum, Limits limits, ref int length) {
        if (text.Length > maximum) throw new InvalidDataException("Wire string limit exceeded.");
        int bytes = Utf8.GetByteCount(text);
        if (bytes > maximum) throw new InvalidDataException("Wire string limit exceeded.");
        Add(ref length, 4 + bytes, limits);
    }
    private static void Measure(WireValue value, int depth, Limits limits, ref int length) {
        if (value == null) throw new InvalidDataException("Null wire node.");
        limits.Node(depth);
        Add(ref length, 1, limits);
        switch (value.Kind) {
            case WireKind.Null: break;
            case WireKind.Boolean: Add(ref length, 1, limits); break;
            case WireKind.Number:
                if (!double.IsFinite(value.Number)) throw new InvalidDataException("Non-finite wire number.");
                Add(ref length, 8, limits); break;
            case WireKind.String: MeasureText(value.Text, limits.StringBytes, limits, ref length); break;
            case WireKind.Reference:
                if (value.Handle <= 0 || value.Depth is < 0 or > 8) throw new InvalidDataException("Invalid wire reference.");
                MeasureText(value.Text, 128, limits, ref length); Add(ref length, 12, limits); break;
            case WireKind.Object:
                MeasureText(value.Text, 128, limits, ref length);
                if (value.Fields.Count > limits.Collection) throw new InvalidDataException("Wire record limit exceeded.");
                Add(ref length, 4, limits);
                foreach (var field in value.Fields) {
                    MeasureText(field.Key, 128, limits, ref length);
                    Measure(field.Value, depth + 1, limits, ref length);
                }
                break;
            case WireKind.Array:
                if (value.Items.Count > limits.Collection) throw new InvalidDataException("Wire array limit exceeded.");
                Add(ref length, 4, limits);
                foreach (var item in value.Items) Measure(item, depth + 1, limits, ref length);
                break;
            default: throw new InvalidDataException("Unknown wire node.");
        }
    }
    private static void EncodeText(string text, byte[] bytes, ref int offset) {
        int count = Utf8.GetByteCount(text);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), count); offset += 4;
        offset += Utf8.GetBytes(text.AsSpan(), bytes.AsSpan(offset, count));
    }
    private static void Encode(WireValue value, byte[] bytes, ref int offset) {
        bytes[offset++] = (byte)value.Kind;
        switch (value.Kind) {
            case WireKind.Null: return;
            case WireKind.Boolean: bytes[offset++] = value.Boolean ? (byte)1 : (byte)0; return;
            case WireKind.Number: BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(value.Number)); offset += 8; return;
            case WireKind.String: EncodeText(value.Text, bytes, ref offset); return;
            case WireKind.Reference:
                EncodeText(value.Text, bytes, ref offset);
                BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset, 8), value.Handle); offset += 8;
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value.Depth); offset += 4; return;
            case WireKind.Object:
                EncodeText(value.Text, bytes, ref offset);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value.Fields.Count); offset += 4;
                foreach (var field in value.Fields) { EncodeText(field.Key, bytes, ref offset); Encode(field.Value, bytes, ref offset); }
                return;
            case WireKind.Array:
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value.Items.Count); offset += 4;
                foreach (var item in value.Items) Encode(item, bytes, ref offset);
                return;
        }
    }
    private static ReadOnlySpan<byte> Take(byte[] bytes, ref int offset, int length) {
        if (length < 0 || length > bytes.Length - offset) throw new InvalidDataException("Truncated wire node.");
        var result = bytes.AsSpan(offset, length); offset += length; return result;
    }
    private static int ReadCount(byte[] bytes, ref int offset, int maximum) {
        int count = BinaryPrimitives.ReadInt32LittleEndian(Take(bytes, ref offset, 4));
        if (count < 0 || count > maximum) throw new InvalidDataException("Wire count limit exceeded.");
        return count;
    }
    private static string DecodeText(byte[] bytes, ref int offset, int maximum) {
        int length = ReadCount(bytes, ref offset, maximum);
        return Utf8.GetString(Take(bytes, ref offset, length));
    }
    private static WireValue Decode(byte[] bytes, ref int offset, int depth, Limits limits) {
        limits.Node(depth);
        var kind = (WireKind)Take(bytes, ref offset, 1)[0];
        switch (kind) {
            case WireKind.Null: return WireValue.Null;
            case WireKind.Boolean:
                byte flag = Take(bytes, ref offset, 1)[0];
                return flag <= 1 ? WireValue.Flag(flag == 1) : throw new InvalidDataException("Invalid wire Boolean.");
            case WireKind.Number: return WireValue.Numeric(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Take(bytes, ref offset, 8))));
            case WireKind.String: return WireValue.String(DecodeText(bytes, ref offset, limits.StringBytes));
            case WireKind.Reference: {
                string name = DecodeText(bytes, ref offset, 128);
                long handle = BinaryPrimitives.ReadInt64LittleEndian(Take(bytes, ref offset, 8));
                int referenceDepth = BinaryPrimitives.ReadInt32LittleEndian(Take(bytes, ref offset, 4));
                return WireValue.Reference(handle, name, referenceDepth);
            }
            case WireKind.Object: {
                string name = DecodeText(bytes, ref offset, 128);
                int count = ReadCount(bytes, ref offset, Math.Min(limits.Collection, limits.Nodes - limits.UsedNodes));
                // Each field needs at least a four-byte name length and a one-byte value.
                if (count > (bytes.Length - offset) / 5) throw new InvalidDataException("Truncated wire record.");
                var fields = new Dictionary<string, WireValue>(count, StringComparer.Ordinal);
                for (int i = 0; i < count; i++) {
                    string key = DecodeText(bytes, ref offset, 128);
                    var value = Decode(bytes, ref offset, depth + 1, limits);
                    if (!fields.TryAdd(key, value)) throw new InvalidDataException("Duplicate wire field.");
                }
                return WireValue.Object(name, fields);
            }
            case WireKind.Array: {
                int count = ReadCount(bytes, ref offset, Math.Min(limits.Collection, limits.Nodes - limits.UsedNodes));
                if (count > bytes.Length - offset) throw new InvalidDataException("Truncated wire array.");
                var items = new WireValue[count];
                for (int i = 0; i < count; i++) items[i] = Decode(bytes, ref offset, depth + 1, limits);
                return WireValue.Array(items);
            }
            default: throw new InvalidDataException("Unknown wire node.");
        }
    }
    private static void ReadExactly(Stream stream, Span<byte> bytes) {
        while (!bytes.IsEmpty) {
            int count = stream.Read(bytes);
            if (count == 0) throw new EndOfStreamException("Truncated worker frame.");
            bytes = bytes.Slice(count);
        }
    }
}
