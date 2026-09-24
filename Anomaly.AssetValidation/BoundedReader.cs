using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Anomaly.AssetValidation;

internal readonly struct ByteSlice {
    internal ByteSlice(byte[] bytes, int offset, int count) { Bytes = bytes; Offset = offset; Count = count; }
    internal readonly byte[] Bytes;
    internal readonly int Offset;
    internal readonly int Count;
}

internal sealed class BoundedReader {
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    internal readonly byte[] Bytes;
    internal readonly int End;
    internal int Position;
    internal bool BigEndian;
    private readonly CancellationToken _cancellation;
    private int _operations;

    internal BoundedReader(byte[] bytes, int offset, int length, CancellationToken cancellation = default, bool bigEndian = false) {
        Guard.Require(offset >= 0 && length >= 0 && offset <= bytes.Length - length, "Invalid byte range.");
        Bytes = bytes; Position = offset; End = offset + length; _cancellation = cancellation; BigEndian = bigEndian;
    }
    internal int Remaining => End - Position;
    internal void Need(int count) {
        if ((++_operations & 1023) == 0) _cancellation.ThrowIfCancellationRequested();
        Guard.Require(count >= 0 && count <= Remaining, "Truncated data.");
    }
    internal byte U8() { Need(1); return Bytes[Position++]; }
    internal bool Boolean() { var value = U8(); Guard.Require(value <= 1, "Invalid boolean."); return value != 0; }
    internal ushort U16() {
        Need(2); var p = Position; Position += 2;
        return BigEndian ? (ushort)((Bytes[p] << 8) | Bytes[p + 1]) : (ushort)(Bytes[p] | (Bytes[p + 1] << 8));
    }
    internal uint U32() {
        Need(4); var p = Position; Position += 4;
        return BigEndian ? ((uint)Bytes[p] << 24) | ((uint)Bytes[p + 1] << 16) | ((uint)Bytes[p + 2] << 8) | Bytes[p + 3]
            : Bytes[p] | ((uint)Bytes[p + 1] << 8) | ((uint)Bytes[p + 2] << 16) | ((uint)Bytes[p + 3] << 24);
    }
    internal int I32() => unchecked((int)U32());
    internal ulong U64() { var first = U32(); var second = U32(); return BigEndian ? ((ulong)first << 32) | second : first | ((ulong)second << 32); }
    internal long I64() => unchecked((long)U64());
    internal double Number32() { var value = new FloatBits { Bits = U32() }.Value; Guard.Require(!float.IsNaN(value) && !float.IsInfinity(value), "Non-finite number."); return value; }
    internal double Number64() { var value = new DoubleBits { Bits = U64() }.Value; Guard.Require(!double.IsNaN(value) && !double.IsInfinity(value), "Non-finite number."); return value; }
    internal ByteSlice Slice(int count) { Need(count); var result = new ByteSlice(Bytes, Position, count); Position += count; return result; }
    internal void Zero(int count) { Need(count); for (var i = 0; i < count; i++) Guard.Require(U8() == 0, "Nonzero padding or reserved data."); }
    internal void Align(int alignment = 4) { Zero((-Position) & (alignment - 1)); }
    internal string NullString(int maximum) {
        var start = Position;
        while (true) { Guard.Require(Position - start <= maximum, "String exceeds limit."); if (U8() == 0) return Decode(start, Position - start - 1); }
    }
    internal string String(int maximum = 1024) {
        var count = I32(); Guard.Require(count >= 0 && count <= maximum, "String exceeds limit.");
        var bytes = Slice(count); var result = Decode(bytes.Offset, bytes.Count); Align();
        Guard.Require(result.IndexOf('\0') < 0, "Embedded null in string."); return result;
    }
    private string Decode(int start, int count) {
        try { return Utf8.GetString(Bytes, start, count); }
        catch (DecoderFallbackException) { throw new BundleValidationException("Invalid UTF-8."); }
    }
    internal void Match(byte[] expected) {
        Need(expected.Length);
        for (var i = 0; i < expected.Length; i++) Guard.Require(Bytes[Position + i] == expected[i], "Type schema differs from the supported profile.");
        Position += expected.Length;
    }
    internal void Finished() => Guard.Require(Position == End, "Unconsumed data.");
    [StructLayout(LayoutKind.Explicit)] private struct FloatBits { [FieldOffset(0)] public uint Bits; [FieldOffset(0)] public float Value; }
    [StructLayout(LayoutKind.Explicit)] private struct DoubleBits { [FieldOffset(0)] public ulong Bits; [FieldOffset(0)] public double Value; }
}
