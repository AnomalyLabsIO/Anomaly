using System.Text;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Anomaly.Client.Scripting.Messaging;

internal sealed record ScriptMessageContext(string PackId, string IndexSha256, ulong ActivationId,
    IReadOnlyList<ScriptMessageSchema> Schemas, Func<bool> IsCurrent, Func<ScriptToServerMessage, bool>? Send);

/// <summary>A pack-bound capability. Neither transport objects nor CLR message types enter Lua.</summary>
internal sealed class ScriptMessages : IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly ScriptMessageContext _context;
    private readonly Script _owner;
    private readonly Func<Func<IQueuedScriptInvocation>, int, bool> _enqueue;
    private readonly ScriptSubscriptionBudget _subscriptions;
    private readonly Dictionary<string, Registration> _registrations;
    private readonly Dictionary<string, ScriptMessageSchema> _schemas;
    private bool _disposed;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;

    internal ScriptMessages(ScriptMessageContext context, Script owner,
        Func<Func<IQueuedScriptInvocation>, int, bool> enqueue, ScriptSubscriptionBudget subscriptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(enqueue);
        ArgumentNullException.ThrowIfNull(subscriptions);
        if (context.Schemas == null || context.Schemas.Count > ScriptMessageLimits.MaxSchemasPerPack)
            throw new ArgumentException("Invalid script message capability context.", nameof(context));
        using var memoryScope = owner.MemoryBudget?.EnterScope();
        // Canonical pack validation uses bounded CLR substring/regex workspace.
        using var validationMemory = MemoryBudget.Reserve(4096);
        if (!AssetPackIndexCodec.IsPackId(context.PackId, remote: false) ||
            !AssetPackIndexCodec.IsHash(context.IndexSha256) || context.IsCurrent == null)
            throw new ArgumentException("Invalid script message capability context.", nameof(context));
        // Keep only the frozen contracts below. Retaining the producer's mutable
        // list as well would keep later additions alive outside the session quota.
        _context = MemoryBudget.Allocate(256, () => context with { Schemas = Array.Empty<ScriptMessageSchema>() });
        _owner = owner;
        _enqueue = enqueue;
        _subscriptions = subscriptions;
        _registrations = MemoryBudget.Allocate(4224, () => new Dictionary<string, Registration>(32, StringComparer.Ordinal));
        _schemas = MemoryBudget.Allocate(128L + context.Schemas.Count * 128L,
            () => new Dictionary<string, ScriptMessageSchema>(context.Schemas.Count, StringComparer.Ordinal));
        foreach (var schema in context.Schemas)
        {
            if (schema == null) throw new ArgumentException("A message contract is null.", nameof(context));
            schema.Validate();
            // The copy belongs to the session and survives mutation of producer metadata.
            var copy = MemoryBudget.Allocate(512L + schema.Fields.Count * 256L, schema.Clone);
            if (!_schemas.TryAdd(copy.Name, copy))
                throw new ArgumentException("A message contract name is duplicated.", nameof(context));
        }
    }

    internal void InstallGlobal()
    {
        EnsureOwner(_owner);
        if (!_owner.Globals.Get("Messages").IsNil()) throw new InvalidOperationException("Messages is already installed.");
        var descriptor = MemoryBudget.Allocate(256, () => new Descriptor(this));
        _owner.Globals.Set("Messages", UserData.Create(this, descriptor));
    }

    internal bool Receive(ScriptToClientMessage message)
    {
        EnsureOwner(_owner);
        if (_disposed || !_context.IsCurrent() || _context.ActivationId == 0 ||
            message.PackId != _context.PackId || message.IndexSha256 != _context.IndexSha256 ||
            message.ActivationId != _context.ActivationId || !_schemas.TryGetValue(message.Name, out var schema) ||
            schema.Direction != AnomalyMessageDirection.ServerToClient) return false;
        try { schema.ValidatePayload(message.Payload); }
        catch (InvalidDataException) { return false; }
        // Each registration gets its own ordinary Lua record at dispatch time. The
        // session reserves queue bytes and allocation allowance before this copy.
        foreach (var registration in _registrations.Values)
        {
            if (!registration.IsActive || registration.Name != message.Name) continue;
            if (!_enqueue(() => new Invocation(this, registration, Snapshot(message.Payload)), message.Payload.EncodedBytes))
                return false;
        }
        return true;
    }

    private static ScriptMessagePayload Snapshot(ScriptMessagePayload payload)
    {
        var values = new Dictionary<string, object>(payload.Values.Count, StringComparer.Ordinal);
        foreach (var pair in payload.Values)
            values.Add(pair.Key, pair.Value is string text ? new string(text.AsSpan()) : pair.Value);
        return ScriptMessagePayload.FromValues(values);
    }

    private string On(string name, DynValue callback)
    {
        EnsureOwner(_owner);
        if (!_context.IsCurrent()) throw new ScriptRuntimeException("The message capability is no longer current.");
        if (!_schemas.TryGetValue(name, out var schema) || schema.Direction != AnomalyMessageDirection.ServerToClient)
            throw new ScriptRuntimeException("The message is outside this pack's approved inbound contracts.");
        if (callback.Type != DataType.Function || !ReferenceEquals(callback.Function.OwnerScript, _owner))
            throw new ScriptRuntimeException("A message callback must be a Lua function owned by this session.");
        using var reservation = MemoryBudget.Reserve(2048);
        var token = MemoryBudget.AllocateString(32, () => Guid.NewGuid().ToString("N"));
        var registration = new Registration(name, token, callback.Clone());
        if (!_subscriptions.TryAcquire())
            throw new ScriptRuntimeException("The session exceeded its combined subscription limit.");
        _registrations.Add(token, registration);
        reservation.Bind(registration);
        return token;
    }

    private bool Off(DynValue token)
    {
        EnsureOwner(_owner);
        if (token.Type != DataType.String || token.String.Length != 32 ||
            !_registrations.Remove(token.String, out var registration)) return false;
        registration.Deactivate();
        _subscriptions.Release();
        return true;
    }

    private DynValue Send(string name, DynValue record)
    {
        EnsureOwner(_owner);
        if (!_schemas.TryGetValue(name, out var schema)) return Outcome(false, "UnknownMessage");
        if (schema.Direction != AnomalyMessageDirection.ClientToServer) return Outcome(false, "WrongDirection");
        if (!_context.IsCurrent() || _context.ActivationId == 0 || _context.Send == null)
            return Outcome(false, "NotReady");
        ScriptMessagePayload payload;
        // Reserve the full bounded CLR snapshot before enumerating or copying Lua
        // data. Keeping an accepted envelope alive retains its allocation ticket.
        using var reservation = MemoryBudget.Reserve(32 * 1024);
        try { payload = ReadRecord(schema, record); }
        catch (Exception exception) when (exception is InvalidDataException or EncoderFallbackException)
        { return Outcome(false, "InvalidPayload"); }
        if (!_context.IsCurrent()) return Outcome(false, "NotReady");
        var envelope = MemoryBudget.Allocate(512, () => ScriptToServerMessage.Create(_context.PackId, name, payload)
            .Bind(_context.IndexSha256, _context.ActivationId));
        // A transport observer may retain only the primitive Values view, rather
        // than the envelope. That view still owns the copied dictionary/storage.
        reservation.Bind(payload.Values);
        try { return _context.Send(envelope) ? Outcome(true, null) : Outcome(false, "NotReady"); }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not ScriptExecutionAbortedException)
        { return Outcome(false, "NotReady"); }
    }

    private static ScriptMessagePayload ReadRecord(ScriptMessageSchema schema, DynValue record)
    {
        if (record.Type != DataType.Table || record.Table.MetaTable != null)
            throw new InvalidDataException("Message payloads require plain records.");
        // Iterate the raw table. Never use indexing that can execute a metamethod.
        var count = 0;
        foreach (var pair in record.Table.Pairs)
        {
            if (++count > schema.Fields.Count || pair.Key.Type != DataType.String)
                throw new InvalidDataException("Message fields do not match the approved schema.");
            var known = false;
            foreach (var field in schema.Fields)
                if (field.Name == pair.Key.String) { known = true; break; }
            if (!known) throw new InvalidDataException("Unexpected message field.");
        }
        if (count != schema.Fields.Count) throw new InvalidDataException("Required message fields are missing.");
        var values = new Dictionary<string, object>(schema.Fields.Count, StringComparer.Ordinal);
        var encodedBytes = 1;
        foreach (var field in schema.Fields)
        {
            var value = record.Table.RawGet(field.Name) ?? DynValue.Nil;
            object converted = field.Type switch
            {
                "Boolean" when value.Type == DataType.Boolean => value.Boolean,
                "Int32" when IsInteger(value, int.MinValue, int.MaxValue) => (int)value.Number,
                "UInt32" when IsInteger(value, uint.MinValue, uint.MaxValue) => (uint)value.Number,
                "Number" when value.Type == DataType.Number && double.IsFinite(value.Number) => value.Number,
                "String" when value.Type == DataType.String && value.String.Length <= field.MaxBytes &&
                    value.String.IndexOf('\0') < 0 && Utf8.GetByteCount(value.String) <= field.MaxBytes =>
                    MemoryBudget.AllocateString(value.String.Length, () => new string(value.String.AsSpan())),
                _ => throw new InvalidDataException("Message field has an invalid primitive type or bound.")
            };
            var valueBytes = converted is bool ? 1 : converted is int or uint ? 4 : converted is double ? 8 :
                2 + Utf8.GetByteCount((string)converted);
            encodedBytes = checked(encodedBytes + 1 + field.Name.Length + 1 + valueBytes);
            if (encodedBytes > ScriptMessagePayload.MaxEncodedBytes)
                throw new InvalidDataException("Message payload exceeds its encoded byte limit.");
            values.Add(field.Name, converted);
        }
        var payload = ScriptMessagePayload.FromValues(values);
        schema.ValidatePayload(payload);
        return payload;
    }

    private static bool IsInteger(DynValue value, double minimum, double maximum) => value.Type == DataType.Number &&
        double.IsFinite(value.Number) && value.Number >= minimum && value.Number <= maximum && value.Number == Math.Truncate(value.Number);
    private static DynValue Outcome(bool accepted, string? error) => DynValue.NewTuple(DynValue.NewBoolean(accepted),
        error == null ? DynValue.Nil : DynValue.NewString(error));

    private void EnsureOwner(Script script)
    {
        if (Environment.CurrentManagedThreadId != _ownerThread || !ReferenceEquals(script, _owner))
            throw new ScriptRuntimeException("The message capability belongs to another script.");
        script.ExecutionBudget?.Check();
        script.MemoryBudget?.Check();
        if (_disposed) throw new ScriptRuntimeException("The message capability has been revoked.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var registration in _registrations.Values) { registration.Deactivate(); _subscriptions.Release(); }
        _registrations.Clear();
        _schemas.Clear();
    }

    private sealed class Registration
    {
        internal Registration(string name, string token, DynValue callback) { Name = name; Token = token; Callback = callback; }
        internal string Name { get; }
        internal string Token { get; }
        internal DynValue Callback { get; private set; }
        internal bool IsActive { get; private set; } = true;
        internal void Deactivate() { IsActive = false; Callback = DynValue.Nil; }
    }

    private sealed class Invocation : IQueuedScriptInvocation
    {
        private readonly ScriptMessages _owner;
        private readonly Registration _registration;
        private readonly ScriptMessagePayload _payload;
        internal Invocation(ScriptMessages owner, Registration registration, ScriptMessagePayload payload)
        { _owner = owner; _registration = registration; _payload = payload; }
        public bool IsActive => !_owner._disposed && _registration.IsActive && _owner._context.IsCurrent();
        public DynValue Callback => _registration.Callback;
        public DynValue[] ResolveArguments(Script script)
        {
            _owner.EnsureOwner(script);
            var table = new Table(script);
            foreach (var pair in _payload.Values)
            {
                DynValue value = pair.Value switch
                {
                    string text => DynValue.NewString(text),
                    bool boolean => DynValue.NewBoolean(boolean),
                    int integer => DynValue.NewNumber(integer),
                    uint integer => DynValue.NewNumber(integer),
                    double number => DynValue.NewNumber(number),
                    _ => throw new ScriptRuntimeException("Invalid queued primitive message field.")
                };
                table.Set(pair.Key, value);
            }
            return MemoryBudget.Allocate(40, () => new[] { DynValue.NewTable(table) });
        }
    }

    private sealed class Descriptor : IUserDataDescriptor
    {
        private readonly ScriptMessages _messages;
        private DynValue? _on, _off, _send;
        internal Descriptor(ScriptMessages messages) { _messages = messages; }
        public string Name => "Messages";
        public Type Type => typeof(ScriptMessages);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            Check(script, obj);
            if (index.Type != DataType.String || index.String is not ("On" or "Off" or "Send")) return DynValue.Nil;
            ref var cached = ref (index.String == "On" ? ref _on : ref (index.String == "Off" ? ref _off : ref _send));
            if (cached != null) return cached;
            var operation = MemoryBudget.Allocate(128, () => new Operation(this, index.String));
            var callback = MemoryBudget.Allocate(96, () => new Func<ScriptExecutionContext, CallbackArguments, DynValue>(operation.Invoke));
            // These three wrappers are immutable capabilities. Repeated field
            // lookups must not manufacture a new CLR closure and diagnostic name.
            return cached = DynValue.NewCallback(callback, index.String == "On" ? "Messages.On" :
                index.String == "Off" ? "Messages.Off" : "Messages.Send");
        }
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
        { Check(script, obj); throw new ScriptRuntimeException("The Messages capability is read-only."); }
        public string AsString(object obj) { Check(_messages._owner, obj); return "[script Messages]"; }
        public DynValue MetaIndex(Script script, object obj, string metaname) { Check(script, obj); return null!; }
        public bool IsTypeCompatible(Type type, object obj) => false;
        private void Check(Script script, object obj)
        {
            _messages.EnsureOwner(script);
            if (!ReferenceEquals(obj, _messages)) throw new ScriptRuntimeException("Invalid message capability receiver.");
        }
        private sealed class Operation
        {
            private readonly Descriptor _descriptor;
            private readonly string _name;
            internal Operation(Descriptor descriptor, string name) { _descriptor = descriptor; _name = name; }
            internal DynValue Invoke(ScriptExecutionContext context, CallbackArguments arguments)
            {
                var messages = _descriptor._messages;
                _descriptor.Check(context.GetScript(), messages);
                var offset = arguments.IsMethodCall ? 1 : 0;
                var count = _name == "Off" ? 1 : 2;
                if (arguments.Count != count + offset || (offset == 1 && (arguments[0].Type != DataType.UserData ||
                    !ReferenceEquals(arguments[0].UserData.Descriptor, _descriptor) ||
                    !ReferenceEquals(arguments[0].UserData.Object, messages))))
                    throw new ScriptRuntimeException("The message operation received an invalid receiver or argument count.");
                if (_name == "Off") return DynValue.NewBoolean(messages.Off(arguments[offset]));
                if (arguments[offset].Type != DataType.String || arguments[offset].String.Length > ScriptMessageSchema.MaxNameLength)
                    throw new ScriptRuntimeException("A message name must be a bounded string.");
                var name = arguments[offset].String;
                return _name == "On" ? DynValue.NewString(messages.On(name, arguments[offset + 1])) :
                    messages.Send(name, arguments[offset + 1]);
            }
        }
    }
}
