using System.Globalization;
using Anomaly.Client.Scripting.Remote.Protocol;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Anomaly.Client.ScriptWorker;

internal sealed partial class WorkerSession
{
    private DynValue FromWire(WireValue value, int depth = 0, bool frozen = false)
    {
        try { return ReadWire(value, depth, frozen); }
        catch (InvalidDataException exception) { throw ProtocolFailure(exception.Message); }
    }

    private DynValue ReadWire(WireValue value, int depth, bool frozen)
    {
        if (depth > 8) throw new InvalidDataException("Host value depth exceeded.");
        switch (value.Kind)
        {
            case WireKind.Null: return DynValue.Nil;
            case WireKind.Boolean: return DynValue.NewBoolean(value.Boolean);
            case WireKind.Number:
                if (!double.IsFinite(value.Number)) throw new InvalidDataException("Non-finite host number.");
                return DynValue.NewNumber(value.Number);
            case WireKind.String:
                value.RequireString(4096);
                return DynValue.NewString(value.Text);
            case WireKind.Reference:
                if (frozen || !_catalog.Types.ContainsKey(value.Text) || value.Depth > 8 || value.Handle <= 0 ||
                    value.Handle == 1 && value.Text != "GameContext" || value.Handle == 2 && value.Text != "ScriptOutput")
                    throw new InvalidDataException("Invalid host reference.");
                if (_proxies.TryGetValue(value.Handle, out var existing) && existing.TryGetTarget(out var retained))
                {
                    if (retained.Alias != value.Text || retained.Depth != value.Depth) throw new InvalidDataException("Host reference changed identity.");
                    return retained.Value;
                }
                if (!_proxies.ContainsKey(value.Handle))
                {
                    if (_proxies.Count >= 1024) throw ProtocolFailure("Live host reference limit exceeded.");
                    _proxyHandles.Add(value.Handle);
                }
                var proxy = MemoryBudget.Allocate(192, () => new Proxy(this, value.Handle, value.Text, value.Depth));
                proxy.Value = UserData.Create(proxy, proxy);
                _proxies[value.Handle] = MemoryBudget.Allocate(32, () => new WeakReference<Proxy>(proxy));
                return proxy.Value;
            case WireKind.Array:
                var array = new Table(Script);
                foreach (var item in value.RequireArray(256)) array.Append(FromWire(item, depth + 1, frozen));
                return DynValue.NewTable(array);
            case WireKind.Object:
                if (value.Text.Length != 0)
                {
                    if (!_catalog.Types.ContainsKey(value.Text)) throw new InvalidDataException("Unknown frozen type.");
                    _memory.Track(value, RetainedWireBytes(value));
                    return NewNode(value.Text, key => value.Fields.TryGetValue(key, out var member) ? FromWire(member, depth + 1, frozen: true) : DynValue.Nil);
                }
                var record = new Table(Script);
                foreach (var field in value.RequireObject(256)) record.Set(field.Key, FromWire(field.Value, depth + 1, frozen));
                return DynValue.NewTable(record);
            default: throw new InvalidDataException("Unsupported host value.");
        }
    }

    private ScriptExecutionAbortedException ProtocolFailure(string error)
    {
        ProtocolError = Wire.Error(error);
        Script.ExecutionBudget?.Cancel();
        return new ScriptExecutionAbortedException(ExecutionTerminationReason.Cancelled);
    }

    private static long RetainedWireBytes(WireValue value)
    {
        long bytes = 96 + 2L * value.Text.Length;
        foreach (var field in value.Fields) bytes += 64 + 2L * field.Key.Length + RetainedWireBytes(field.Value);
        foreach (var item in value.Items) bytes += 8 + RetainedWireBytes(item);
        return bytes;
    }

    private WireValue ToWireArgument(DynValue value)
    {
        if (value.Type == DataType.UserData && value.UserData.Object is Proxy proxy && ReferenceEquals(proxy.Owner, this))
        {
            Check(Script);
            return WireValue.Reference(proxy.Handle, proxy.Alias, proxy.Depth);
        }
        return ToWireScalar(value);
    }

    private sealed class Proxy : IUserDataDescriptor
    {
        internal readonly WorkerSession Owner;
        internal readonly long Handle;
        internal readonly string Alias;
        internal readonly int Depth;
        internal DynValue Value = DynValue.Nil;
        private readonly Dictionary<string, DynValue> _methods;
        internal Proxy(WorkerSession owner, long handle, string alias, int depth)
        {
            Owner = owner; Handle = handle; Alias = alias; Depth = depth;
            _methods = MemoryBudget.Allocate(256, () => new Dictionary<string, DynValue>(StringComparer.Ordinal));
        }
        public string Name => Alias;
        public Type Type => typeof(Proxy);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            Check(script, obj);
            if (index.Type != DataType.String || !Owner._catalog.Types[Alias].TryGetValue(index.String, out var member)) return DynValue.Nil;
            if (!member.Method)
            {
                var request = Wire.Request("Get");
                request.Add("target", WireValue.Numeric(Handle));
                request.Add("member", WireValue.String(member.Name));
                request.Add("args", WireValue.Array(Array.Empty<WireValue>()));
                var result = Owner.Rpc(request);
                GC.KeepAlive(this);
                return Owner.FromWire(result);
            }
            if (_methods.TryGetValue(member.Name, out var cached)) return cached;
            using var reservation = MemoryBudget.Reserve(512);
            var callback = DynValue.NewCallback((context, args) => Invoke(context, args, member), Alias + "." + member.Name);
            reservation.Bind(callback);
            _methods.Add(member.Name, callback);
            return callback;
        }
        private DynValue Invoke(ScriptExecutionContext context, CallbackArguments args, WorkerCatalog.Member member)
        {
            Check(context.GetScript(), this);
            var offset = args.IsMethodCall ? 1 : 0;
            var valid = args.Count == member.ArgumentCount + offset && (offset == 0 || args[0].Type == DataType.UserData &&
                ReferenceEquals(args[0].UserData.Object, this) && ReferenceEquals(args[0].UserData.Descriptor, this));
            var output = Handle == 2;
            if (!valid)
            {
                if (output) return DynValue.False;
                throw new ScriptRuntimeException("The host operation received an invalid receiver or argument count.");
            }
            if (output && !ValidOutput(member.Name, args, offset)) return DynValue.False;
            var converted = MemoryBudget.Allocate(32L + member.ArgumentCount * 8L, () => new WireValue[member.ArgumentCount]);
            for (var i = 0; i < converted.Length; i++) converted[i] = Owner.ToWireArgument(args[i + offset]);
            var request = Wire.Request("Call");
            request.Add("target", WireValue.Numeric(Handle));
            request.Add("member", WireValue.String(member.Name));
            request.Add("args", WireValue.Array(converted));
            var result = Owner.Rpc(request);
            GC.KeepAlive(this);
            GC.KeepAlive(args);
            return Owner.FromWire(result);
        }
        private static bool ValidOutput(string member, CallbackArguments args, int offset)
        {
            if (member == "ClearText") return true;
            if (member is not ("Log" or "ShowText") || args[offset].Type != DataType.String ||
                !ValidText(args[offset].String, member == "ShowText")) return false;
            return member != "ShowText" || args[offset + 1].Type == DataType.Number && double.IsFinite(args[offset + 1].Number) &&
                args[offset + 1].Number is >= 0.1 and <= 10;
        }
        private static bool ValidText(string text, bool display)
        {
            if (text.Length is 0 or > 1024) return false;
            var lines = 1;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (display && c == '\n') { if (++lines > 8) return false; continue; }
                var category = CharUnicodeInfo.GetUnicodeCategory(text, i);
                if (char.IsControl(c) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                    return false;
            }
            try { return RemoteProtocol.Utf8.GetByteCount(text) <= 1024; }
            catch (System.Text.EncoderFallbackException) { return false; }
        }
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
        { Check(script, obj); throw new ScriptRuntimeException("Host properties are read-only."); }
        public string AsString(object obj)
        {
            Check(Owner.Script, obj);
            return MemoryBudget.AllocateString(9 + Alias.Length, () => "[script " + Alias + "]");
        }
        public DynValue MetaIndex(Script script, object obj, string metaname) { Check(script, obj); return null!; }
        public bool IsTypeCompatible(Type type, object obj) => false;
        private void Check(Script script, object obj)
        {
            Owner.Check(script);
            if (!ReferenceEquals(obj, this)) throw new ScriptRuntimeException("Invalid host capability receiver.");
        }
    }
}
