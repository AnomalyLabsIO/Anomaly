using System.Globalization;
using System.Text;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Events;
using Anomaly.Client.Scripting.Output;
using Anomaly.Client.Scripting.Remote.Protocol;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting.Remote;

internal sealed record RemoteDelivery(string Token, WireValue Arguments);

/// <summary>Parent-owned capabilities and frozen callback queue; never creates a Lua VM.</summary>
internal sealed class RemoteHostSession : IDisposable, IScriptEventPublicationOwner
{
    private const int MaximumHandles = 1024;
    private const int MaximumSubscriptions = 32;
    private const int MaximumQueue = 64;
    private const int MaximumMessageBytes = 64 * 1024;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly ScriptBindingRegistry _bindings;
    private readonly ScriptEventCatalog? _events;
    private readonly MemoryBudget _memory;
    private readonly ScriptOutput _output;
    private readonly Func<bool> _isCurrent;
    private readonly Func<ScriptToServerMessage, bool>? _send;
    private readonly string _packId;
    private readonly string? _indexSha256;
    private readonly ulong _activationId;
    private readonly Dictionary<Type, ScriptTypePolicy> _policies;
    private readonly Dictionary<long, LiveHandle> _handles = new(MaximumHandles);
    private readonly Dictionary<string, ScriptEventRegistration> _eventRegistrations = new(MaximumSubscriptions, StringComparer.Ordinal);
    private readonly Dictionary<string, MessageRegistration> _messageRegistrations = new(MaximumSubscriptions, StringComparer.Ordinal);
    private readonly Dictionary<string, ScriptMessageSchema> _schemas = new(StringComparer.Ordinal);
    private readonly LinkedList<QueuedDelivery> _queue = new();
    private long _nextHandle = 2;
    private int _queuedMessageBytes;
    private int _faulted;
    private bool _disposed;

    internal RemoteHostSession(ScriptRuntime runtime, ScriptSource source, ulong activationId, Func<bool> isCurrent,
        Func<ScriptToServerMessage, bool>? send)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(isCurrent);
        using var runtimeScope = runtime.EnterAllocationScope();
        source.Validate();
        if (activationId == 0) throw new InvalidDataException("A remote capability requires an activation identity.");
        _bindings = runtime.Bindings;
        _policies = RemoteHostValues.ApprovedPolicies(runtime).ToDictionary(policy => policy.Type);
        _events = runtime.EventCatalog;
        _isCurrent = isCurrent;
        _send = send;
        _packId = source.PackId;
        _indexSha256 = source.IndexSha256;
        _activationId = activationId;
        _memory = new MemoryBudget(16L * 1024 * 1024, runtime.HostMemory, reclaimable: true);
        using var memoryScope = _memory.EnterScope();
        using var storage = _memory.ReserveAllocation(128 * 1024);
        storage.Bind(this);
        if (source.MessageSchemas != null)
            foreach (var schema in source.MessageSchemas)
            {
                var copy = schema.Clone();
                if (!_schemas.TryAdd(copy.Name, copy)) throw new InvalidDataException("Duplicate message grant.");
            }
        var schemas = new WireValue[_schemas.Count];
        var schemaIndex = 0;
        foreach (var schema in _schemas.Values)
        {
            var fields = new WireValue[schema.Fields.Count];
            for (var index = 0; index < fields.Length; index++)
            {
                var field = schema.Fields[index];
                fields[index] = RemoteHostValues.Record(("name", WireValue.String(field.Name)),
                    ("type", WireValue.String(field.Type)), ("maxBytes", WireValue.Numeric(field.MaxBytes)));
            }
            schemas[schemaIndex++] = RemoteHostValues.Record(("name", WireValue.String(schema.Name)),
                ("direction", WireValue.Numeric((int)schema.Direction)), ("fields", WireValue.Array(fields)));
        }
        LoadData = RemoteHostValues.Record(("source", WireValue.String(source.Source)), ("address", WireValue.String(source.Address)),
            ("schemas", WireValue.Array(schemas)));
        _output = runtime.CreateRemoteOutput(_memory, source.PackId + " " + source.Address);
        if (_policies.TryGetValue(typeof(ScriptGameContext), out var gamePolicy))
            _handles.Add(1, new LiveHandle(new ScriptGameContext(), gamePolicy, 0));
        _handles.Add(2, new LiveHandle(_output, _policies[typeof(ScriptOutput)], 0));
        EnsureCurrent();
    }

    internal static WireValue ExportCatalog(ScriptRuntime runtime) => RemoteHostValues.ExportCatalog(runtime);
    internal WireValue LoadData { get; }
    internal bool IsHealthy
    {
        get
        {
            if (_disposed || Volatile.Read(ref _faulted) != 0 || _events?.IsDisposed == true) return false;
            try
            {
                if (_isCurrent()) return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException) { }
            RequestRetirement();
            return false;
        }
    }

    internal WireValue Dispatch(WireValue request)
    {
        EnsureOwner();
        try
        {
            EnsureCurrent();
            using var memoryScope = _memory.EnterScope();
            _memory.Check();
            _memory.Sweep();
            RemoteHostValues.CheckRequest(request);
            var operation = request.Required("op").RequireString(32);
            // Validate all shape before consuming releases or entering native code.
            switch (operation)
            {
                case "Get": case "Call": RemoteHostValues.Shape(request, "op", "target", "member", "args", "release"); break;
                case "Subscribe": RemoteHostValues.Shape(request, "op", "event", "release"); break;
                case "Unsubscribe": case "MessageOff": RemoteHostValues.Shape(request, "op", "token", "release"); break;
                case "MessageOn": RemoteHostValues.Shape(request, "op", "name", "release"); break;
                case "Send": RemoteHostValues.Shape(request, "op", "name", "payload", "release"); break;
                default: throw new InvalidDataException("Unknown host operation.");
            }
            Release(request.Required("release"));
            WireValue value;
            using (ScriptHostAccess.Enter())
            {
                value = operation switch
                {
                    "Get" => Access(request, call: false),
                    "Call" => Access(request, call: true),
                    "Subscribe" => Subscribe(request.Required("event").RequireString(255)),
                    "Unsubscribe" => WireValue.Flag(Unsubscribe(request.Required("token").RequireString(32))),
                    "MessageOn" => MessageOn(request.Required("name").RequireString(ScriptMessageSchema.MaxNameLength)),
                    "MessageOff" => WireValue.Flag(MessageOff(request.Required("token").RequireString(32))),
                    "Send" => Send(request.Required("name").RequireString(ScriptMessageSchema.MaxNameLength), request.Required("payload")),
                    _ => throw new InvalidDataException("Unknown host operation.")
                };
                ScriptHostAccess.ThrowIfFailed();
            }
            EnsureCurrent();
            _memory.Check();
            return RemoteHostValues.Record(("ok", WireValue.True), ("value", value));
        }
        catch (Exception original)
        {
            var exception = RemoteHostValues.Unwrap(original);
            if (exception is OutOfMemoryException or StackOverflowException)
            {
                RequestRetirement();
                RemoteHostValues.Rethrow(exception);
            }
            if (exception is InvalidDataException or ScriptHostAccessException or ScriptExecutionAbortedException || !IsHealthy)
            {
                RequestRetirement();
                throw new InvalidDataException("The remote host capability was retired.", exception);
            }
            return RemoteHostValues.Record(("ok", WireValue.False), ("value", WireValue.Null),
                ("error", WireValue.String("The approved host operation failed.")));
        }
    }

    private WireValue Access(WireValue request, bool call)
    {
        var target = request.Required("target").RequireInteger(1, 9007199254740991L);
        if (!_handles.TryGetValue(target, out var handle)) throw new InvalidDataException("Unknown host target.");
        var name = request.Required("member").RequireString(128);
        if (!handle.Policy.Members.TryGetValue(name, out var member) || member.AllowWrite || (member.Method != null) != call)
            throw new InvalidDataException("The operation is outside the approved host policy.");
        var arguments = request.Required("args").RequireArray(16);
        if (arguments.Count != member.Parameters.Length) throw new InvalidDataException("Invalid host argument count.");
        if (handle.Value is ScriptOutput output) return Output(output, member.Name, arguments);
        object? result;
        if (call)
        {
            var converted = new object?[arguments.Count];
            for (var index = 0; index < converted.Length; index++)
                converted[index] = Argument(arguments[index], member.Parameters[index].ParameterType);
            result = member.Method!.Invoke(handle.Value, converted);
        }
        else result = member.Property!.GetValue(handle.Value);
        ScriptHostAccess.ThrowIfFailed();
        EnsureCurrent();
        return Value(result, member.ReturnType, handle.Depth + 1);
    }

    private static WireValue Output(ScriptOutput output, string member, IReadOnlyList<WireValue> arguments)
    {
        // Revalidate the borrowed-text/false-on-invalid contract independently of
        // the worker, without a generic argument conversion or a local Script.
        switch (member)
        {
            case nameof(ScriptOutput.ClearText): output.ClearText(); return WireValue.Null;
            case nameof(ScriptOutput.Log):
                return WireValue.Flag(arguments[0].Kind == WireKind.String && output.Log(arguments[0].Text));
            case nameof(ScriptOutput.ShowText):
                return WireValue.Flag(arguments[0].Kind == WireKind.String && arguments[1].Kind == WireKind.Number &&
                    output.ShowText(arguments[0].Text, arguments[1].Number));
            default: throw new InvalidDataException("Unknown output policy.");
        }
    }

    private WireValue Value(object? value, Type declaredType, int depth)
    {
        if (value == null) return WireValue.Null;
        declaredType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        var actualType = value.GetType();
        if (!_bindings.IsApprovedValue(actualType, declaredType)) throw new InvalidDataException("Unapproved host result type.");
        if (declaredType == typeof(string)) return WireValue.String(RemoteHostValues.CheckString((string)value));
        if (declaredType == typeof(bool)) return WireValue.Flag((bool)value);
        if (declaredType.IsEnum) return WireValue.String(RemoteHostValues.CheckString(_bindings.Enums.Format(declaredType, value)));
        if (RemoteHostValues.IsNumeric(declaredType))
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            try { RemoteHostValues.CheckNumber(number, declaredType); }
            catch (ScriptRuntimeException exception) { throw new InvalidDataException("Invalid host numeric result.", exception); }
            return WireValue.Numeric(number);
        }
        if (depth > ScriptBindingRegistry.MaxObjectDepth || !_policies.TryGetValue(declaredType, out var policy) ||
            !_policies.ContainsKey(actualType) || _handles.Count >= MaximumHandles || _nextHandle >= 9007199254740991L)
            throw new InvalidDataException("Host object depth, type or handle limit exceeded.");
        using var storage = _memory.ReserveAllocation(2048);
        var retained = new LiveHandle(value, policy, depth);
        storage.Bind(retained);
        var id = ++_nextHandle;
        _handles.Add(id, retained);
        return WireValue.Reference(id, policy.Name, depth);
    }

    private object? Argument(WireValue value, Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (value.Kind == WireKind.Null && (nullable != null || type == typeof(string) || !type.IsValueType)) return null;
        type = nullable ?? type;
        if (type == typeof(string))
        {
            if (value.Kind != WireKind.String) throw new ScriptRuntimeException("Expected a string argument.");
            return value.RequireString();
        }
        if (type == typeof(bool))
        {
            if (value.Kind != WireKind.Boolean) throw new ScriptRuntimeException("Expected a Boolean argument.");
            return value.Boolean;
        }
        if (type.IsEnum)
        {
            if (value.Kind != WireKind.String) throw new ScriptRuntimeException("Expected an enum argument.");
            return _bindings.Enums.Parse(type, value.RequireString());
        }
        if (RemoteHostValues.IsNumeric(type))
        {
            if (value.Kind != WireKind.Number) throw new ScriptRuntimeException("Expected a number argument.");
            RemoteHostValues.CheckNumber(value.Number, type);
            try { return Convert.ChangeType(value.Number, type, CultureInfo.InvariantCulture); }
            catch (OverflowException) { throw new ScriptRuntimeException("The numeric argument is outside its declared range."); }
        }
        if (value.Kind != WireKind.Reference || !_handles.TryGetValue(value.Handle, out var handle) ||
            handle.Policy.Name != value.Text || handle.Depth != value.Depth || handle.Policy.Type != type ||
            !_bindings.IsApprovedValue(handle.Value.GetType(), type))
            throw new InvalidDataException("Invalid typed host argument reference.");
        return handle.Value;
    }

    private void Release(WireValue release)
    {
        var handles = release.RequireArray(64);
        foreach (var value in handles)
        {
            var id = value.RequireInteger(3, 9007199254740991L);
            if (!_handles.Remove(id)) throw new InvalidDataException("Unknown or duplicate released host handle.");
        }
    }

    private WireValue Subscribe(string name)
    {
        var separator = name.IndexOf('.');
        if (separator <= 0 || separator != name.LastIndexOf('.') || _events == null ||
            !_events.Categories.TryGetValue(name[..separator], out var category) ||
            !category.TryGetValue(name[(separator + 1)..], out var source))
            throw new InvalidDataException("The event is outside the approved catalog.");
        CheckSubscriptionLimit();
        var token = Guid.NewGuid().ToString("N");
        var registration = new ScriptEventRegistration(this, source, token);
        _eventRegistrations.Add(token, registration);
        try { source.Add(registration); }
        catch
        {
            _eventRegistrations.Remove(token);
            registration.Deactivate();
            try { source.Remove(registration); } catch (Exception) { RequestRetirement(); }
            RequestRetirement();
            throw;
        }
        return WireValue.String(token);
    }

    private bool Unsubscribe(string token)
    {
        if (!_eventRegistrations.Remove(token, out var registration)) return false;
        RemoveQueued(token);
        try { registration.Source.Remove(registration); }
        catch { registration.Deactivate(); RequestRetirement(); throw; }
        return true;
    }

    private WireValue MessageOn(string name)
    {
        if (!_schemas.TryGetValue(name, out var schema) || schema.Direction != AnomalyMessageDirection.ServerToClient)
            throw new ScriptRuntimeException("The message is outside the approved inbound contracts.");
        CheckSubscriptionLimit();
        var token = Guid.NewGuid().ToString("N");
        _messageRegistrations.Add(token, new MessageRegistration(name));
        return WireValue.String(token);
    }

    private bool MessageOff(string token)
    {
        if (!_messageRegistrations.Remove(token)) return false;
        RemoveQueued(token);
        return true;
    }

    private void CheckSubscriptionLimit()
    {
        if (_eventRegistrations.Count + _messageRegistrations.Count >= MaximumSubscriptions)
            throw new InvalidDataException("The remote session exceeded its combined subscription limit.");
    }

    private WireValue Send(string name, WireValue record)
    {
        if (!_schemas.TryGetValue(name, out var schema)) return Outcome(false, "UnknownMessage");
        if (schema.Direction != AnomalyMessageDirection.ClientToServer) return Outcome(false, "WrongDirection");
        if (!IsHealthy || _activationId == 0 || _indexSha256 == null || _send == null) return Outcome(false, "NotReady");
        ScriptMessagePayload payload;
        using var reservation = _memory.ReserveAllocation(32 * 1024);
        try { payload = Payload(schema, record); }
        catch (Exception exception) when (exception is InvalidDataException or EncoderFallbackException)
        { return Outcome(false, "InvalidPayload"); }
        reservation.Bind(payload.Values);
        EnsureCurrent();
        var envelope = ScriptToServerMessage.Create(_packId, name, payload).Bind(_indexSha256, _activationId);
        try { return _send(envelope) ? Outcome(true, null) : Outcome(false, "NotReady"); }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not ScriptExecutionAbortedException)
        { return Outcome(false, "NotReady"); }
    }

    private static ScriptMessagePayload Payload(ScriptMessageSchema schema, WireValue record)
    {
        if (record.Kind != WireKind.Object || record.Text.Length != 0 || record.Fields.Count != schema.Fields.Count)
            throw new InvalidDataException("Messages require exact primitive records.");
        var values = new Dictionary<string, object>(schema.Fields.Count, StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            var value = record.Required(field.Name);
            object converted = field.Type switch
            {
                "Boolean" when value.Kind == WireKind.Boolean => value.Boolean,
                "Int32" => checked((int)value.RequireInteger(int.MinValue, int.MaxValue)),
                "UInt32" => checked((uint)value.RequireInteger(uint.MinValue, uint.MaxValue)),
                "Number" when value.Kind == WireKind.Number && double.IsFinite(value.Number) => value.Number,
                "String" when value.Kind == WireKind.String => value.RequireString(field.MaxBytes),
                _ => throw new InvalidDataException("Invalid message field type.")
            };
            values.Add(field.Name, converted);
        }
        var payload = ScriptMessagePayload.FromValues(values);
        schema.ValidatePayload(payload);
        return payload;
    }

    internal bool Receive(ScriptToClientMessage message)
    {
        EnsureOwner();
        if (!IsHealthy || message == null || _activationId == 0 || message.PackId != _packId ||
            message.IndexSha256 != _indexSha256 || message.ActivationId != _activationId ||
            !_schemas.TryGetValue(message.Name, out var schema) || schema.Direction != AnomalyMessageDirection.ServerToClient) return false;
        try
        {
            schema.ValidatePayload(message.Payload);
            using var scope = _memory.EnterScope();
            foreach (var registration in _messageRegistrations)
            {
                if (registration.Value.Name != message.Name) continue;
                var bytes = message.Payload.EncodedBytes;
                ReserveQueue(bytes);
                using var memory = _memory.ReserveAllocation(32 * 1024);
                var fields = new Dictionary<string, WireValue>(message.Payload.Values.Count, StringComparer.Ordinal);
                foreach (var pair in message.Payload.Values)
                    fields.Add(pair.Key, pair.Value switch
                    {
                        bool flag => WireValue.Flag(flag),
                        int number => WireValue.Numeric(number),
                        uint number => WireValue.Numeric(number),
                        double number when double.IsFinite(number) => WireValue.Numeric(number),
                        string text => WireValue.String(new string(text.AsSpan())),
                        _ => throw new InvalidDataException("Invalid message primitive.")
                    });
                var arguments = WireValue.Array(new[] { WireValue.Object(string.Empty, fields) });
                memory.Bind(arguments);
                _queue.AddLast(new QueuedDelivery(registration.Key, arguments, null, bytes));
                _queuedMessageBytes += bytes;
            }
            return true;
        }
        catch (InvalidDataException) { return false; }
        catch (Exception exception)
        {
            RequestRetirement();
            ScriptHostException.RethrowFatal(exception);
            return false;
        }
    }

    internal bool TryDequeue(out RemoteDelivery delivery)
    {
        EnsureOwner();
        delivery = null!;
        if (!IsHealthy) return false;
        while (_queue.First is { } first)
        {
            var queued = first.Value;
            _queue.RemoveFirst();
            _queuedMessageBytes -= queued.MessageBytes;
            if (queued.Event is { } eventRegistration ? !eventRegistration.IsActive : !_messageRegistrations.ContainsKey(queued.Token)) continue;
            delivery = new RemoteDelivery(queued.Token, queued.Arguments);
            return true;
        }
        return false;
    }

    private static WireValue Outcome(bool accepted, string? reason) => WireValue.Array(new[]
        { WireValue.Flag(accepted), reason == null ? WireValue.Null : WireValue.String(reason) });

    private void ReserveQueue(int messageBytes)
    {
        if (_queue.Count >= MaximumQueue || messageBytes < 0 || messageBytes > MaximumMessageBytes - _queuedMessageBytes)
        {
            RequestRetirement();
            throw new InvalidDataException("The remote callback queue exceeded its allowance.");
        }
    }

    private void RemoveQueued(string token)
    {
        var node = _queue.First;
        while (node != null)
        {
            var next = node.Next;
            if (node.Value.Token == token)
            {
                _queuedMessageBytes -= node.Value.MessageBytes;
                _queue.Remove(node);
            }
            node = next;
        }
    }

    bool IScriptEventPublicationOwner.IsUnavailable => _disposed || Volatile.Read(ref _faulted) != 0;
    bool IScriptEventPublicationOwner.RequiresNativeScope => true;
    void IScriptEventPublicationOwner.EnsureOwner(Script script) => throw new InvalidOperationException("Remote event ownership has no interpreter.");
    void IScriptEventPublicationOwner.RequestRetirement(string reason, string? eventName) => RequestRetirement();

    bool IScriptEventPublicationOwner.TryReservePublication(ScriptEventRegistration registration,
        out MemoryBudget.AllocationReservation reservation)
    {
        reservation = default;
        try
        {
            EnsureOwner();
            EnsureCurrent();
            var replacing = false;
            if (registration.Source.CoalesceLatest)
                for (var node = _queue.First; node != null; node = node.Next)
                    if (ReferenceEquals(node.Value.Event, registration)) { replacing = true; break; }
            if (!replacing) ReserveQueue(0);
            reservation = _memory.ReserveAllocation(64 * 1024);
            return true;
        }
        catch (Exception exception)
        {
            RequestRetirement();
            ScriptHostException.RethrowFatal(exception);
            return false;
        }
    }

    void IScriptEventPublicationOwner.Publish(ScriptEventRegistration registration, object?[] arguments, object allocationOwner)
    {
        EnsureOwner();
        EnsureCurrent();
        if (!registration.IsActive) return;
        using var scope = _memory.EnterScope();
        using var reservation = _memory.ReserveAllocation(64 * 1024);
        var values = new WireValue[arguments.Length];
        for (var index = 0; index < values.Length; index++) values[index] = RemoteHostValues.Frozen(arguments[index]);
        var converted = WireValue.Array(values);
        reservation.Bind(converted);
        if (registration.Source.CoalesceLatest)
            for (var node = _queue.First; node != null; node = node.Next)
                if (ReferenceEquals(node.Value.Event, registration))
                {
                    node.Value = new QueuedDelivery(registration.Token, converted, registration, 0);
                    return;
                }
        ReserveQueue(0);
        _queue.AddLast(new QueuedDelivery(registration.Token, converted, registration, 0));
        GC.KeepAlive(allocationOwner);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Remote host capabilities must be accessed on the owner thread.");
    }

    private void EnsureCurrent()
    {
        if (!IsHealthy) throw new InvalidDataException("The remote host capability is no longer current.");
    }

    private void RequestRetirement() => Interlocked.Exchange(ref _faulted, 1);

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        RequestRetirement();
        using var nativeScope = ScriptHostAccess.Enter();
        Exception? failure = null;
        foreach (var registration in _eventRegistrations.Values)
        {
            try { registration.Source.Remove(registration); }
            catch (Exception exception) { registration.Deactivate(); failure ??= exception; }
        }
        _eventRegistrations.Clear();
        _messageRegistrations.Clear();
        _queue.Clear();
        _queuedMessageBytes = 0;
        _schemas.Clear();
        _handles.Clear();
        try { _output.Dispose(); ScriptHostAccess.ThrowIfFailed(); }
        catch (Exception exception) { failure ??= exception; }
        if (failure != null) throw new InvalidOperationException("A remote host capability could not be revoked.", failure);
    }

    private sealed record LiveHandle(object Value, ScriptTypePolicy Policy, int Depth);
    private sealed record MessageRegistration(string Name);
    private sealed record QueuedDelivery(string Token, WireValue Arguments, ScriptEventRegistration? Event, int MessageBytes);
}
