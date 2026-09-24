using System;
using System.Collections.Generic;
using System.Threading;
using AssetsTools.NET;

namespace Anomaly.AssetValidation;

internal sealed class ObjectVisitor {
    private readonly BoundedReader _reader;
    private readonly CancellationToken _cancellation;
    private readonly List<AssetPointer> _pointers;
    private int _nodes;
    private ObjectVisitor(ByteSlice bytes, List<AssetPointer> pointers, CancellationToken cancellation) {
        _reader = new BoundedReader(bytes.Bytes, bytes.Offset, bytes.Count, cancellation);
        _pointers = pointers; _cancellation = cancellation;
    }
    internal static Value Read(AssetRecord record, List<AssetPointer> pointers, CancellationToken cancellation) {
        var visitor = new ObjectVisitor(record.Bytes, pointers, cancellation);
        var value = visitor.Visit(CanonicalProfile.Types[record.ClassId].Template, 0);
        visitor._reader.Finished(); return value;
    }
    private Value Visit(AssetTypeTemplateField template, int depth) {
        _cancellation.ThrowIfCancellationRequested();
        Guard.Require(depth <= 32 && ++_nodes <= 65536, "Object complexity exceeds limit.");
        var value = new Value(template.Name) { Offset = _reader.Position };
        switch (template.ValueType) {
            case AssetValueType.Bool: value.Integer = _reader.Boolean() ? 1 : 0; break;
            case AssetValueType.Int8: value.Integer = unchecked((sbyte)_reader.U8()); break;
            case AssetValueType.UInt8: value.Integer = _reader.U8(); break;
            case AssetValueType.Int16: value.Integer = unchecked((short)_reader.U16()); break;
            case AssetValueType.UInt16: value.Integer = _reader.U16(); break;
            case AssetValueType.Int32: value.Integer = _reader.I32(); break;
            case AssetValueType.UInt32: value.Integer = _reader.U32(); break;
            case AssetValueType.Int64: value.Integer = _reader.I64(); break;
            case AssetValueType.UInt64:
                var unsigned = _reader.U64(); Guard.Require(unsigned <= long.MaxValue, "Unsigned field exceeds supported range."); value.Integer = (long)unsigned; break;
            case AssetValueType.Float: value.Number = _reader.Number32(); break;
            case AssetValueType.Double: value.Number = _reader.Number64(); break;
            case AssetValueType.String: value.Text = _reader.String(); break;
            case AssetValueType.ByteArray:
                value.Bytes = _reader.Slice(_reader.I32());
                // TypelessData and byte arrays align according to the trusted type tree.
                break;
            case AssetValueType.Array:
                var count = _reader.I32();
                Guard.Require(count >= 0 && count <= 4096 && count <= _reader.Remaining && count <= (65536 - _nodes), "Array count exceeds limit.");
                value.Children = new List<Value>(count);
                for (var i = 0; i < count; i++) value.Children.Add(Visit(template.Children[1], depth + 1));
                break;
            case AssetValueType.None:
                value.Children = new List<Value>(template.Children.Count);
                foreach (var child in template.Children) value.Children.Add(Visit(child, depth + 1));
                break;
            default: throw new BundleValidationException("Unsupported managed or primitive field.");
        }
        if (template.IsAligned) _reader.Align();
        if (template.Type.StartsWith("PPtr<", StringComparison.Ordinal)) {
            Guard.Require(value["m_FileID"].Integer == 0, "External object reference.");
            _pointers.Add(new AssetPointer(value["m_PathID"].Integer, template.Type));
        }
        return value;
    }
}

internal sealed class Value {
    internal Value(string name) { Name = name; }
    internal readonly string Name;
    internal int Offset;
    internal long Integer;
    internal double Number;
    internal string Text;
    internal ByteSlice Bytes;
    internal List<Value> Children;
    internal Value this[string name] {
        get {
            if (Children != null) foreach (var child in Children) if (child.Name == name) return child;
            throw new BundleValidationException("Trusted schema field is missing: " + name);
        }
    }
    internal List<Value> Array => this["Array"].Children;
    internal ByteSlice ByteArray => this["Array"].Bytes;
    internal long I(string field) => this[field].Integer;
    internal double F(string field) => this[field].Number;
    internal string S(string field) => this[field].Text;
    internal void Empty() {
        Guard.Require(Integer == 0 && Number == 0 && string.IsNullOrEmpty(Text) && Bytes.Count == 0, "Unsupported nonempty asset feature.");
        if (Children != null) foreach (var child in Children) child.Empty();
    }
}

internal readonly struct AssetPointer {
    internal AssetPointer(long pathId, string type) { PathId = pathId; Type = type; }
    internal readonly long PathId;
    internal readonly string Type;
}
