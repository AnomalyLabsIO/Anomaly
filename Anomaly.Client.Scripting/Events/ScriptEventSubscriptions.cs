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
    private readonly Dictionary<string, ScriptEventRegistration> _registrations = new(StringComparer.Ordinal);
    private bool _installed;
    private int _disposed;
    private int _faulted;

    internal ScriptEventSubscriptions(ScriptEventCatalog catalog, Script owner, Action<ScriptEventInvocation> enqueue,
        int maxSubscriptions = 32, Action? requestRetirement = null)
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
    }

    internal bool IsUnavailable => Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _faulted) != 0 || _catalog.IsDisposed;
    internal int Count => _registrations.Count;

    internal void InstallGlobal()
    {
        EnsureOwner(_owner);
        if (_installed || !_owner.Globals.Get("Events").IsNil())
            throw new InvalidOperationException("The session's event global is already installed.");
        _owner.Globals.Set("Events", CreateNode(new EventNode(null, null)));
        _installed = true;
    }

    internal string Add(ScriptEventSource source, DynValue callback)
    {
        EnsureOwner(_owner);
        if (!_catalog.Owns(source)) throw new ScriptRuntimeException("The event is outside this session's approved catalog.");
        CheckCallback(callback);
        if (_registrations.Count >= _maxSubscriptions)
            throw new ScriptRuntimeException("The session exceeded its event subscription limit.");
        var token = Guid.NewGuid().ToString("N");
        var registration = new ScriptEventRegistration(this, source, token, callback);
        _registrations.Add(token, registration);
        try { source.Add(registration); }
        catch (Exception exception) when (exception is not OutOfMemoryException && exception is not StackOverflowException)
        {
            _registrations.Remove(token);
            registration.Deactivate();
            try { source.Remove(registration); } catch (Exception) { }
            throw new ScriptRuntimeException("The approved event could not be subscribed.");
        }
        return token;
    }

    internal bool Remove(ScriptEventSource source, DynValue callbackOrToken)
    {
        EnsureOwner(_owner);
        if (!_catalog.Owns(source)) throw new ScriptRuntimeException("The event is outside this session's approved catalog.");
        ScriptEventRegistration[] matches;
        if (callbackOrToken.Type == DataType.String)
        {
            if (callbackOrToken.String.Length != 32) return false;
            matches = _registrations.TryGetValue(callbackOrToken.String, out var registration) && ReferenceEquals(registration.Source, source)
                ? new[] { registration } : Array.Empty<ScriptEventRegistration>();
        }
        else
        {
            CheckCallback(callbackOrToken);
            matches = _registrations.Values.Where(value => ReferenceEquals(value.Source, source) &&
                ReferenceEquals(value.Callback.Function, callbackOrToken.Function)).ToArray();
        }
        foreach (var registration in matches)
        {
            _registrations.Remove(registration.Token);
            try { source.Remove(registration); }
            catch (Exception exception) when (exception is not OutOfMemoryException && exception is not StackOverflowException)
            {
                RequestRetirement();
                throw new ScriptRuntimeException("The approved event could not be detached.");
            }
        }
        return matches.Length > 0;
    }

    internal void Enqueue(ScriptEventInvocation invocation)
    {
        EnsureOwner(_owner);
        if (invocation.IsActive) _enqueue(invocation);
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
        var registrations = _registrations.Values.ToArray();
        _registrations.Clear();
        Exception? failure = null;
        foreach (var registration in registrations)
        {
            try { registration.Source.Remove(registration); }
            catch (Exception exception) { registration.Deactivate(); failure ??= exception; }
        }
        if (failure != null) throw new InvalidOperationException("An event subscription could not be detached.", failure);
    }

    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private DynValue CreateNode(EventNode node) => UserData.Create(node, new Descriptor(this, node));
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
            if (index.Type != DataType.String) return DynValue.Nil;
            if (_node.Source != null)
            {
                if (index.String is not ("Add" or "Remove")) return DynValue.Nil;
                var add = index.String == "Add";
                return DynValue.NewCallback((context, arguments) =>
                {
                    Check(context.GetScript(), _node);
                    var offset = arguments.IsMethodCall ? 1 : 0;
                    if (arguments.Count != 1 + offset || (offset == 1 && (arguments[0].Type != DataType.UserData ||
                        !ReferenceEquals(arguments[0].UserData.Descriptor, this) || !ReferenceEquals(arguments[0].UserData.Object, _node))))
                        throw new ScriptRuntimeException("The event operation received an invalid receiver or argument count.");
                    return add ? DynValue.NewString(_subscriptions.Add(_node.Source, arguments[offset])) :
                        DynValue.NewBoolean(_subscriptions.Remove(_node.Source, arguments[offset]));
                }, _node.Source.QualifiedName + "." + index.String);
            }
            if (_node.Category != null)
                return _subscriptions._catalog.Categories[_node.Category].TryGetValue(index.String, out var source)
                    ? _subscriptions.CreateNode(new EventNode(_node.Category, source)) : DynValue.Nil;
            return _subscriptions._catalog.Categories.ContainsKey(index.String)
                ? _subscriptions.CreateNode(new EventNode(index.String, null)) : DynValue.Nil;
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
