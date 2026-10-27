using System.Text;
using Anomaly.Client.Scripting.Remote.Protocol;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Anomaly.Client.ScriptWorker;

internal sealed partial class WorkerSession
{
    private void InstallCapabilities()
    {
        Script.Globals.Set("Enums", NewNode("Enums", name => _catalog.Enums.TryGetValue(name, out var members) ? EnumNode(name, members) : DynValue.Nil,
            scriptString: false));
        foreach (var global in _catalog.EnumGlobals)
            Script.Globals.Set(global.Key, EnumNode(global.Value, _catalog.Enums[global.Value]));
        Script.Globals.Set("Events", NewNode("Events", category => _catalog.Events.TryGetValue(category, out var members)
            ? NewNode("Events", name => members.TryGetValue(name, out var source) ? EventNode(source) : DynValue.Nil) : DynValue.Nil));
        Node? messages = null;
        var methods = MemoryBudget.Allocate(512, () => new Dictionary<string, DynValue>(3, StringComparer.Ordinal));
        var messageValue = NewNode("Messages", name =>
        {
            if (name is not ("On" or "Off" or "Send")) return DynValue.Nil;
            if (methods.TryGetValue(name, out var cached)) return cached;
            var operation = Bound(messages!, name == "Off" ? 1 : 2, (args, offset) =>
            {
                if (name == "Off") return DynValue.NewBoolean(RemoveMessage(args[offset]));
                if (args[offset].Type != DataType.String || args[offset].String.Length > 64)
                    throw new ScriptRuntimeException("A message name must be a bounded string.");
                var messageName = args[offset].String;
                return name == "On" ? DynValue.NewString(AddMessage(messageName, args[offset + 1])) : SendMessage(messageName, args[offset + 1]);
            });
            methods.Add(name, operation);
            return operation;
        });
        messages = (Node)messageValue.UserData.Object;
        Script.Globals.Set("Messages", messageValue);
    }

    private DynValue EnumNode(string name, IReadOnlyDictionary<string, WireValue> members) => NewNode(name,
        member => members.TryGetValue(member, out var value) ? DynValue.NewString(value.Text) : DynValue.Nil, scriptString: false);

    private DynValue EventNode(WorkerCatalog.Event source)
    {
        Node? node = null;
        var value = NewNode("Events", name => name is "Add" or "Remove" ? Bound(node!, 1, (args, offset) => name == "Add"
            ? DynValue.NewString(AddEvent(source, args[offset])) : DynValue.NewBoolean(RemoveEvent(source, args[offset]))) : DynValue.Nil);
        node = (Node)value.UserData.Object;
        return value;
    }

    private string AddEvent(WorkerCatalog.Event source, DynValue callback)
    {
        CheckCallback(callback);
        EnsureSubscriptionRoom();
        var request = Wire.Request("Subscribe");
        request.Add("event", WireValue.String(source.Name));
        return Register(Rpc(request), source.Name, callback, source.Arity, message: false);
    }

    private string AddMessage(string name, DynValue callback)
    {
        if (!_schemas.TryGetValue(name, out var schema) || schema.Direction != 2)
            throw new ScriptRuntimeException("The message is outside this pack's approved inbound contracts.");
        CheckCallback(callback);
        EnsureSubscriptionRoom();
        var request = Wire.Request("MessageOn");
        request.Add("name", WireValue.String(name));
        return Register(Rpc(request), name, callback, 1, message: true);
    }

    private string Register(WireValue result, string source, DynValue callback, int arity, bool message)
    {
        if (result.Kind != WireKind.String || result.Text.Length != 32 || !Guid.TryParseExact(result.Text, "N", out _) ||
            _registrations.ContainsKey(result.Text))
            throw ProtocolFailure("Host returned an invalid registration token.");
        var token = result.Text;
        var registration = MemoryBudget.Allocate(2048, () => new Registration(token, source, callback.Clone(), arity, message));
        _registrations.Add(token, registration);
        return token;
    }

    private bool RemoveEvent(WorkerCatalog.Event source, DynValue callbackOrToken)
    {
        if (callbackOrToken.Type != DataType.String) CheckCallback(callbackOrToken);
        else if (callbackOrToken.String.Length != 32) return false;
        var matches = MemoryBudget.Allocate(32L + _registrations.Count * 8L, () => new Registration[_registrations.Count]);
        var count = 0;
        foreach (var registration in _registrations.Values)
            if (!registration.Message && registration.Source == source.Name && (callbackOrToken.Type == DataType.String
                ? callbackOrToken.String == registration.Token : ReferenceEquals(callbackOrToken.Function, registration.Callback.Function)))
                matches[count++] = registration;
        for (var i = 0; i < count; i++) RemoveRegistration(matches[i], "Unsubscribe");
        return count > 0;
    }

    private bool RemoveMessage(DynValue token)
    {
        if (token.Type != DataType.String || token.String.Length != 32 || !_registrations.TryGetValue(token.String, out var registration) ||
            !registration.Message) return false;
        RemoveRegistration(registration, "MessageOff");
        return true;
    }

    private void RemoveRegistration(Registration registration, string operation)
    {
        // Invalidate before contacting the host so stale queued invocations cannot retain a callback.
        _registrations.Remove(registration.Token);
        var request = Wire.Request(operation);
        request.Add("token", WireValue.String(registration.Token));
        Rpc(request);
    }

    private DynValue SendMessage(string name, DynValue record)
    {
        if (!_schemas.TryGetValue(name, out var schema)) return Outcome(false, "UnknownMessage");
        if (schema.Direction != 1) return Outcome(false, "WrongDirection");
        using var transient = MemoryBudget.Reserve(32 * 1024);
        WireValue payload;
        try { payload = ReadRecord(schema, record); }
        catch (Exception exception) when (exception is InvalidDataException or EncoderFallbackException)
        { return Outcome(false, "InvalidPayload"); }
        var request = Wire.Request("Send");
        request.Add("name", WireValue.String(name));
        request.Add("payload", payload);
        var response = Rpc(request);
        if (response.Kind != WireKind.Array || response.Items.Count != 2 || response.Items[0].Kind != WireKind.Boolean ||
            response.Items[1].Kind is not (WireKind.Null or WireKind.String) || response.Items[1].Text.Length > 64)
            throw ProtocolFailure("Invalid send result.");
        return DynValue.NewTuple(DynValue.NewBoolean(response.Items[0].Boolean), response.Items[1].Kind == WireKind.Null
            ? DynValue.Nil : DynValue.NewString(response.Items[1].Text));
    }

    private WireValue ReadRecord(MessageSchema schema, DynValue value)
    {
        if (value.Type != DataType.Table || value.Table.MetaTable != null) throw new InvalidDataException("Messages require plain records.");
        var count = 0;
        foreach (var pair in value.Table.Pairs)
        {
            if (++count > schema.Fields.Count || pair.Key.Type != DataType.String || !schema.Fields.Any(field => field.Name == pair.Key.String))
                throw new InvalidDataException("Unexpected message field.");
        }
        if (count != schema.Fields.Count) throw new InvalidDataException("Missing message field.");
        var fields = new Dictionary<string, WireValue>(schema.Fields.Count, StringComparer.Ordinal);
        var bytes = 1;
        foreach (var field in schema.Fields)
        {
            var item = value.Table.RawGet(field.Name) ?? DynValue.Nil;
            var valid = field.Type switch
            {
                "Boolean" => item.Type == DataType.Boolean,
                "Int32" => Integer(item, int.MinValue, int.MaxValue),
                "UInt32" => Integer(item, uint.MinValue, uint.MaxValue),
                "Number" => item.Type == DataType.Number && double.IsFinite(item.Number),
                "String" => item.Type == DataType.String && item.String.Length <= field.MaximumBytes && item.String.IndexOf('\0') < 0 &&
                    RemoteProtocol.Utf8.GetByteCount(item.String) <= field.MaximumBytes,
                _ => false
            };
            if (!valid) throw new InvalidDataException("Invalid message primitive or bound.");
            bytes = checked(bytes + 1 + field.Name.Length + 1 + (field.Type == "Boolean" ? 1 :
                field.Type is "Int32" or "UInt32" ? 4 : field.Type == "Number" ? 8 : 2 + RemoteProtocol.Utf8.GetByteCount(item.String)));
            if (bytes > 4096) throw new InvalidDataException("Message payload exceeds its encoded byte limit.");
            fields.Add(field.Name, ToWireScalar(item));
        }
        return WireValue.Object("", fields);
    }

    private static bool Integer(DynValue value, double min, double max) => value.Type == DataType.Number &&
        double.IsFinite(value.Number) && value.Number >= min && value.Number <= max && Math.Truncate(value.Number) == value.Number;
    private static DynValue Outcome(bool success, string reason) => DynValue.NewTuple(DynValue.NewBoolean(success), DynValue.NewString(reason));
    private void CheckCallback(DynValue callback)
    {
        Check(Script);
        if (callback.Type != DataType.Function || !ReferenceEquals(callback.Function.OwnerScript, Script))
            throw new ScriptRuntimeException("A callback must be a Lua function owned by this session.");
    }
    private void EnsureSubscriptionRoom()
    {
        if (_registrations.Count >= 32) throw new ScriptRuntimeException("The session exceeded its combined subscription limit.");
    }

    private DynValue Bound(Node node, int argc, Func<CallbackArguments, int, DynValue> operation)
    {
        using var reservation = MemoryBudget.Reserve(512);
        var callback = DynValue.NewCallback((context, args) =>
        {
            node.Check(context.GetScript(), node);
            var offset = args.IsMethodCall ? 1 : 0;
            if (args.Count != argc + offset || offset != 0 && (args[0].Type != DataType.UserData ||
                !ReferenceEquals(args[0].UserData.Object, node) || !ReferenceEquals(args[0].UserData.Descriptor, node)))
                throw new ScriptRuntimeException("The operation received an invalid receiver or argument count.");
            return operation(args, offset);
        });
        reservation.Bind(callback);
        return callback;
    }

    private DynValue NewNode(string name, Func<string, DynValue> index, bool scriptString = true)
    {
        var node = MemoryBudget.Allocate(512, () => new Node(this, name, index, scriptString));
        return UserData.Create(node, node);
    }

    private sealed class Node : IUserDataDescriptor
    {
        private readonly WorkerSession _owner;
        private readonly Func<string, DynValue> _index;
        private readonly string _display;
        internal Node(WorkerSession owner, string name, Func<string, DynValue> index, bool scriptString)
        { _owner = owner; Name = name; _index = index; _display = scriptString ? "[script " + name + "]" : name; }
        public string Name { get; }
        public Type Type => typeof(Node);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        { Check(script, obj); return index.Type == DataType.String ? _index(index.String) : DynValue.Nil; }
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
        { Check(script, obj); throw new ScriptRuntimeException("The capability is read-only."); }
        public string AsString(object obj) { Check(_owner.Script, obj); return _display; }
        public DynValue MetaIndex(Script script, object obj, string metaname) { Check(script, obj); return null!; }
        public bool IsTypeCompatible(Type type, object obj) => false;
        internal void Check(Script script, object obj)
        {
            _owner.Check(script);
            if (!ReferenceEquals(obj, this)) throw new ScriptRuntimeException("Invalid capability receiver.");
        }
    }
}
