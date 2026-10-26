using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Anomaly.Client.Scripting.Events;

/// <summary>Session-owned callbacks; CLR publishers enqueue frozen data and never enter the VM.</summary>
internal sealed class ScriptEventSubscriptions : IDisposable
{
    private readonly ScriptEventCatalog _catalog;
    private readonly Script _owner;
    private readonly Action<ScriptEventInvocation> _enqueue;
    private readonly Action? _requestRetirement;
    private readonly int _maxSubscriptions;
    private readonly ScriptSubscriptionBudget? _subscriptionBudget;
    private readonly Dictionary<string, ScriptEventRegistration> _registrations;
    private bool _installed;
    private int _disposed;
    private int _faulted;

    internal ScriptEventSubscriptions(ScriptEventCatalog catalog, Script owner, Action<ScriptEventInvocation> enqueue,
        int maxSubscriptions = 32, Action? requestRetirement = null, ScriptSubscriptionBudget? subscriptionBudget = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(enqueue);
        if (maxSubscriptions < 1 || maxSubscriptions > 128) throw new ArgumentOutOfRangeException(nameof(maxSubscriptions));
        catalog.EnsureOwner();
        _catalog = catalog;
        _owner = owner;
        _enqueue = enqueue;
        _maxSubscriptions = maxSubscriptions;
        _requestRetirement = requestRetirement;
        _subscriptionBudget = subscriptionBudget;
        // Dictionary storage survives Remove/Clear. Reserve its bounded maximum
        // separately from registrations, whose callbacks can be collected earlier.
        using var memoryScope = owner.MemoryBudget?.EnterScope();
        _registrations = MemoryBudget.Allocate(128L + 128L * maxSubscriptions,
            () => new Dictionary<string, ScriptEventRegistration>(maxSubscriptions, StringComparer.Ordinal));
    }

    internal bool IsUnavailable => Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _faulted) != 0 || _catalog.IsDisposed;
    internal int Count => _registrations.Count;

    internal void InstallGlobal()
    {
        EnsureOwner(_owner);
        if (_installed || !_owner.Globals.Get("Events").IsNil())
            throw new InvalidOperationException("The session's event global is already installed.");
        _owner.Globals.Set("Events", CreateNode(null, null));
        _installed = true;
    }

    internal string Add(ScriptEventSource source, DynValue callback)
    {
        EnsureOwner(_owner);
        if (!_catalog.Owns(source)) throw new ScriptRuntimeException("The event is outside this session's approved catalog.");
        CheckCallback(callback);
        if (_registrations.Count >= _maxSubscriptions)
            throw new ScriptRuntimeException("The session exceeded its event subscription limit.");
        using var memoryScope = _owner.MemoryBudget?.EnterScope();
        using var registrationMemory = _owner.MemoryBudget?.ReserveAllocation(2048);
        var token = MemoryBudget.AllocateString(32, () => Guid.NewGuid().ToString("N"));
        var registration = new ScriptEventRegistration(this, source, token, callback);
        registrationMemory?.Bind(registration);
        if (_subscriptionBudget != null && !_subscriptionBudget.TryAcquire())
            throw new ScriptRuntimeException("The session exceeded its combined subscription limit.");
        _registrations.Add(token, registration);
        try { source.Add(registration); }
        catch (Exception exception) when (exception is not OutOfMemoryException && exception is not StackOverflowException)
        {
            _registrations.Remove(token);
            _subscriptionBudget?.Release();
            registration.Deactivate();
            try { source.Remove(registration); } catch (Exception) { }
            throw new ScriptRuntimeException("The approved event could not be subscribed.");
        }
        return token;
    }

    internal bool Remove(ScriptEventSource source, DynValue callbackOrToken)
    {
        EnsureOwner(_owner);
        using var memoryScope = _owner.MemoryBudget?.EnterScope();
        if (!_catalog.Owns(source)) throw new ScriptRuntimeException("The event is outside this session's approved catalog.");
        if (callbackOrToken.Type != DataType.String) CheckCallback(callbackOrToken);
        else if (callbackOrToken.String.Length != 32) return false;
        if (_registrations.Count == 0) return false;
        var matches = MemoryBudget.Allocate(32L + 8L * _registrations.Count,
            () => new ScriptEventRegistration[_registrations.Count]);
        var count = 0;
        foreach (var candidate in _registrations.Values)
            if (ReferenceEquals(candidate.Source, source) && (callbackOrToken.Type == DataType.String
                ? candidate.Token == callbackOrToken.String : ReferenceEquals(candidate.Callback.Function, callbackOrToken.Function)))
                matches[count++] = candidate;
        for (var index = 0; index < count; index++)
        {
            var registration = matches[index];
            _registrations.Remove(registration.Token);
            _subscriptionBudget?.Release();
            try { source.Remove(registration); }
            catch (Exception exception) when (exception is not OutOfMemoryException && exception is not StackOverflowException)
            {
                RequestRetirement();
                throw new ScriptRuntimeException("The approved event could not be detached.");
            }
        }
        return count > 0;
    }

    internal void Enqueue(ScriptEventInvocation invocation)
    {
        EnsureOwner(_owner);
        if (invocation.IsActive) _enqueue(invocation);
    }

    // Reserve the maximum bounded snapshot and delivery bookkeeping before a
    // publisher captures it. Each recipient pays even when the copy is shared.
    internal bool TryReservePublication(out MemoryBudget.AllocationReservation reservation)
    {
        reservation = default;
        try
        {
            reservation = _owner.MemoryBudget?.ReserveAllocation(64 * 1024) ?? default;
            return true;
        }
        catch (ScriptExecutionAbortedException)
        {
            RequestRetirement();
            return false;
        }
    }

    // Callable from a CLR publishing thread. Only signal; teardown and Lua access
    // remain on the session owner thread, even when the source fires off-thread.
    internal void RequestRetirement()
    {
        if (Interlocked.Exchange(ref _faulted, 1) != 0) return;
        try { _requestRetirement?.Invoke(); }
        catch (Exception) { }
    }

    internal void EnsureOwner(Script script)
    {
        _catalog.EnsureOwner();
        if (!ReferenceEquals(script, _owner)) throw new ScriptRuntimeException("The event binding belongs to another script.");
        script.ExecutionBudget?.Check();
        script.MemoryBudget?.Check();
        if (IsUnavailable) throw new ScriptRuntimeException("The session's event subscriptions are unavailable.");
    }

    private void CheckCallback(DynValue callback)
    {
        if (callback.Type != DataType.Function || !ReferenceEquals(callback.Function.OwnerScript, _owner))
            throw new ScriptRuntimeException("An event callback must be a Lua function owned by this session.");
    }

    public void Dispose()
    {
        // A disposed catalog still permits resource cleanup without touching Lua.
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Event subscriptions must be disposed on their owner thread.");
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Exception? failure = null;
        while (_registrations.Count != 0)
        {
            using var iterator = _registrations.Values.GetEnumerator();
            iterator.MoveNext();
            var registration = iterator.Current;
            _registrations.Remove(registration.Token);
            _subscriptionBudget?.Release();
            try { registration.Source.Remove(registration); }
            catch (Exception exception) { registration.Deactivate(); failure ??= exception; }
        }
        if (failure != null) throw failure;
    }

    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private DynValue CreateNode(string? category, ScriptEventSource? source)
    {
        using var memoryScope = _owner.MemoryBudget?.EnterScope();
        var node = MemoryBudget.Allocate(128, () => new EventNode(category, source));
        var descriptor = MemoryBudget.Allocate(256, () => new Descriptor(this, node));
        return UserData.Create(node, descriptor);
    }
    private sealed record EventNode(string? Category, ScriptEventSource? Source);

    private sealed class Descriptor : IUserDataDescriptor
    {
        private readonly ScriptEventSubscriptions _subscriptions;
        private readonly EventNode _node;
        internal Descriptor(ScriptEventSubscriptions subscriptions, EventNode node) { _subscriptions = subscriptions; _node = node; }
        public string Name => "Events";
        public Type Type => typeof(EventNode);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            Check(script, obj);
            using var memoryScope = script.MemoryBudget?.EnterScope();
            if (index.Type != DataType.String) return DynValue.Nil;
            if (_node.Source != null)
            {
                if (index.String is not ("Add" or "Remove")) return DynValue.Nil;
                var add = index.String == "Add";
                var operation = MemoryBudget.Allocate(192, () => new EventOperation(this, add));
                var callback = MemoryBudget.Allocate(96,
                    () => new Func<ScriptExecutionContext, CallbackArguments, DynValue>(operation.Invoke));
                return DynValue.NewCallback(callback, add ? _node.Source.AddName : _node.Source.RemoveName);
            }
            if (_node.Category != null)
                return _subscriptions._catalog.Categories[_node.Category].TryGetValue(index.String, out var source)
                    ? _subscriptions.CreateNode(_node.Category, source) : DynValue.Nil;
            return _subscriptions._catalog.Categories.ContainsKey(index.String)
                ? _subscriptions.CreateNode(index.String, null) : DynValue.Nil;
        }
        private sealed class EventOperation
        {
            private readonly Descriptor _descriptor;
            private readonly bool _add;
            internal EventOperation(Descriptor descriptor, bool add) { _descriptor = descriptor; _add = add; }
            internal DynValue Invoke(ScriptExecutionContext context, CallbackArguments arguments)
            {
                var subscriptions = _descriptor._subscriptions;
                var node = _descriptor._node;
                _descriptor.Check(context.GetScript(), node);
                var offset = arguments.IsMethodCall ? 1 : 0;
                if (arguments.Count != 1 + offset || (offset == 1 && (arguments[0].Type != DataType.UserData ||
                    !ReferenceEquals(arguments[0].UserData.Descriptor, _descriptor) || !ReferenceEquals(arguments[0].UserData.Object, node))))
                    throw new ScriptRuntimeException("The event operation received an invalid receiver or argument count.");
                return _add ? DynValue.NewString(subscriptions.Add(node.Source!, arguments[offset])) :
                    DynValue.NewBoolean(subscriptions.Remove(node.Source!, arguments[offset]));
            }
        }
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
        {
            Check(script, obj);
            throw new ScriptRuntimeException("The approved event catalog is read-only.");
        }
        public string AsString(object obj) { Check(_subscriptions._owner, obj); return "[script Events]"; }
        public DynValue MetaIndex(Script script, object obj, string metaname) { Check(script, obj); return null!; }
        public bool IsTypeCompatible(Type type, object obj) => false;
        private void Check(Script script, object obj)
        {
            _subscriptions.EnsureOwner(script);
            if (!ReferenceEquals(obj, _node)) throw new ScriptRuntimeException("Invalid event binding receiver.");
        }
    }
}
