using System.Text;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Events;
using Anomaly.Client.Scripting.Messaging;
using Anomaly.Shared.Networking.Messages;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Loaders;
using MoonSharp.Interpreter.Platforms;

namespace Anomaly.Client.Scripting;

/// <summary>
/// Development and approved script-pack boundary. All entry points are internal; raw remote
/// source is rejected before construction. Accounted allocations have a reclaimable allowance;
/// this is not a qualified CLR heap quota.
/// </summary>
internal sealed class ScriptSession : IDisposable
{
    private static readonly HashSet<string> AllowedGlobals = new(StringComparer.Ordinal)
    {
        "_G", "_VERSION", "assert", "error", "select", "type", "tonumber", "tostring",
        "ipairs", "pairs", "next", "pcall", "xpcall", "math", "table", "string"
    };
    private readonly ScriptBindingRegistry _bindings;
    private readonly ScriptExecutionOptions _options;
    private readonly MemoryBudget _memory;
    private readonly Action<string>? _diagnostic;
    private const int MaxResources = 32;
    private static readonly ScriptExecutionResult CompletedResult = new(ScriptExecutionStatus.Completed, "Completed.");
    private static readonly ScriptExecutionResult RetiredResult = new(ScriptExecutionStatus.Retired, "The script session has been retired.");
    private static readonly ScriptExecutionResult InvalidResult = new(ScriptExecutionStatus.InvalidResult, "Entry points may return only bounded primitive values.");
    private static readonly ScriptExecutionResult HostFailureResult = new(ScriptExecutionStatus.ScriptError, "The script host rejected an operation.");
    private static readonly ScriptExecutionResult[] TerminatedResults = Enum.GetValues<ExecutionTerminationReason>()
        .Select(reason => new ScriptExecutionResult(reason switch
        {
            ExecutionTerminationReason.CpuLimit => ScriptExecutionStatus.CpuLimit,
            ExecutionTerminationReason.Deadline => ScriptExecutionStatus.Deadline,
            ExecutionTerminationReason.DepthLimit => ScriptExecutionStatus.DepthLimit,
            ExecutionTerminationReason.MemoryLimit => ScriptExecutionStatus.MemoryLimit,
            _ => ScriptExecutionStatus.Cancelled
        }, "Script execution terminated: " + reason)).ToArray();
    private readonly List<IDisposable> _resources;
    private readonly Queue<QueuedScriptEvent> _events;
    private readonly ScriptSubscriptionBudget _subscriptionBudget;
    private ScriptMessages? _messages;
    private int _queuedMessageBytes;
    private readonly ExecutionBudget _retiredBudget;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private Script? _script;
    private bool _loaded;
    private bool _executing;
    private bool _retired;
    private int _retirementRequested;
    private ExecutionBudget? _activeBudget;

    internal ScriptSession(ScriptBindingRegistry bindings, ScriptExecutionOptions options, Action<string>? diagnostic,
        ScriptEventCatalog? eventCatalog = null, MemoryBudget? runtimeMemory = null)
    {
        options.Validate();
        _bindings = bindings;
        _options = options;
        _diagnostic = diagnostic;
        _memory = new MemoryBudget(options.AllocationLimitBytes, runtimeMemory, reclaimable: true);
        using var memoryScope = _memory.EnterScope();
        // Preallocate bounded backing stores. Clear/Dequeue cannot refund storage
        // that the live collection still retains.
        _resources = MemoryBudget.Allocate(64L + MaxResources * 8L, () => new List<IDisposable>(MaxResources));
        _events = MemoryBudget.Allocate(64L + 64L * 8L, () => new Queue<QueuedScriptEvent>(64));
        _subscriptionBudget = MemoryBudget.Allocate(32, () => new ScriptSubscriptionBudget());
        _retiredBudget = MemoryBudget.Allocate(192, () => new ExecutionBudget(1, TimeSpan.FromMilliseconds(1)));
        _retiredBudget.Cancel();

        // These globals belong to the privately named fork, not another mod's engine.
        UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;
        Script.GlobalOptions.Platform = new LimitedPlatformAccessor();
        var script = new Script(CoreModules.None, _memory);
        script.Options.ScriptLoader = new RejectingLoader();
        script.Globals.RegisterCoreModules(CoreModules.GlobalConsts | CoreModules.TableIterators |
            CoreModules.Basic | CoreModules.Math | CoreModules.Table | CoreModules.String | CoreModules.ErrorHandling);
        script.Globals.Get("string").Table.Remove("dump");
        var keys = MemoryBudget.Allocate(32L + script.Globals.Length * 8L + 4096, () => script.Globals.Keys.ToArray());
        foreach (var key in keys)
            if (key.Type != DataType.String || !AllowedGlobals.Contains(key.String)) script.Globals.Remove(key);
        _script = script;
        _bindings.Enums.InstallGlobals(script);
        if (_bindings.HasGameContext)
            _bindings.SetGlobal(script, "Game", MemoryBudget.Allocate(32, () => new ScriptGameContext()));
        if (eventCatalog != null)
        {
            var subscriptions = MemoryBudget.Allocate(512, () => new ScriptEventSubscriptions(eventCatalog, script,
                invocation => QueueCallback(invocation), requestRetirement: SignalRetirement,
                subscriptionBudget: _subscriptionBudget));
            _resources.Add(subscriptions);
            try { subscriptions.InstallGlobal(); }
            catch
            {
                Retire();
                throw;
            }
        }
    }

    internal bool IsRetired => _retired;
    internal long MemoryChargedBytes => _memory.ChargedBytes;
    internal long MemoryTotalAllocatedBytes => _memory.TotalAllocatedBytes;
    internal long MemoryUnboundBytes => _memory.UnboundBytes;
    internal int QueuedMessageBytes => _queuedMessageBytes;
    internal int QueuedCount => _events.Count;

    internal void InstallMessages(ScriptMessageContext context)
    {
        EnsureAvailable();
        if (_loaded || _executing || _messages != null)
            throw new InvalidOperationException("Install message capabilities before loading the script.");
        using var scope = _memory.EnterScope();
        _messages = MemoryBudget.Allocate(1024, () => new ScriptMessages(context, _script!,
            QueueMessage, _subscriptionBudget));
        _resources.Add(_messages);
        _messages.InstallGlobal();
    }

    internal bool ReceiveMessage(ScriptToClientMessage message)
    {
        EnsureOwner();
        ApplyRetirementRequest();
        if (_retired || !_loaded || _messages == null) return false;
        try
        {
            using var memoryScope = _memory.EnterScope();
            // Schema validation uses bounded transient CLR bookkeeping; snapshot
            // retention and queue records have their own separately bound tickets.
            using var transient = MemoryBudget.Reserve(16 * 1024);
            return _messages.Receive(message);
        }
        catch (ScriptExecutionAbortedException)
        {
            Retire();
            Report("Script allocation allowance exhausted while receiving a message; session retired.");
            return false;
        }
    }

    internal void SetGlobal(string name, object? value)
    {
        EnsureAvailable();
        if (_executing) throw new InvalidOperationException("Cannot change script globals during execution.");
        if (string.IsNullOrEmpty(name) || name.Length > 64 || name.StartsWith('_') || char.IsDigit(name[0]) ||
            !name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_') ||
            AllowedGlobals.Contains(name) || _bindings.Enums.IsGlobalName(name) || name is "Game" or "Events" or "Messages" or "io" or "os" or "package" or "debug" or "require" or "load" or "loadfile" or "dofile")
            throw new ArgumentException("A host global must use a non-reserved simple name.", nameof(name));
        try
        {
            using var memoryScope = _memory.EnterScope();
            _memory.Sweep();
            _bindings.SetGlobal(_script!, name, value);
            _memory.Check();
        }
        catch (ScriptExecutionAbortedException)
        {
            Retire();
            throw;
        }
        catch (Exception exception) when (exception is OutOfMemoryException or StackOverflowException)
        {
            SignalRetirement();
            throw;
        }
    }

    internal void TrackResource(IDisposable resource)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(resource);
        if (_resources.Count >= MaxResources) throw new InvalidOperationException("The script resource limit has been reached.");
        _resources.Add(resource);
    }

    internal ScriptExecutionResult Load(string? source, string? sourceFriendlyName = null)
    {
        EnsureOwner();
        ApplyRetirementRequest();
        if (_retired) return Retired();
        if (_loaded) throw new InvalidOperationException("A session loads exactly one source package.");
        var rejection = ScriptSourcePolicy.Reject(source, _options);
        if (rejection != null)
        {
            Retire();
            return new ScriptExecutionResult(ScriptExecutionStatus.SourceRejected, rejection);
        }
        if (sourceFriendlyName != null && (sourceFriendlyName.Length == 0 || sourceFriendlyName.Length > 255 ||
            sourceFriendlyName.Any(character => char.IsControl(character))))
            throw new ArgumentException("Invalid script source diagnostic name.", nameof(sourceFriendlyName));
        var result = Execute(() => _script!.LoadString(source!, codeFriendlyName: sourceFriendlyName ?? "anomaly-script"));
        _loaded = result.Succeeded;
        return result;
    }

    internal ScriptExecutionResult Invoke(string functionName, params object?[] arguments)
        => InvokeWithin(functionName, arguments, _options.Timeout);

    private ScriptExecutionResult InvokeWithin(string functionName, object?[] arguments, TimeSpan timeout)
    {
        EnsureOwner();
        ApplyRetirementRequest();
        if (_retired) return Retired();
        if (!_loaded) throw new InvalidOperationException("Load the session before invoking functions.");
        if (string.IsNullOrEmpty(functionName) || functionName.Length > 64)
            throw new ArgumentException("Invalid function name.", nameof(functionName));
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length > 16) throw new ArgumentException("Entry points accept at most 16 host arguments.", nameof(arguments));
        return Execute(() =>
        {
            var function = _script!.Globals.Get(functionName);
            if (function.Type != DataType.Function)
                throw new ScriptRuntimeException("The requested entry point is not a script function.");
            return function;
        }, () => ConvertArguments(arguments), timeout);
    }

    // C# events enqueue data only. No CLR event or Lua delegate is exposed to scripts.
    internal bool QueueEvent(string functionName, params object?[] arguments)
    {
        EnsureOwner();
        ApplyRetirementRequest();
        if (_retired || !_loaded) return false;
        if (string.IsNullOrEmpty(functionName) || functionName.Length > 64 || arguments == null || arguments.Length > 16)
            throw new ArgumentException("Invalid queued script event.");
        return Enqueue(functionName, arguments, null);
    }

    internal bool QueueCallback(ScriptEventInvocation invocation)
    {
        EnsureOwner();
        ApplyRetirementRequest();
        if (_retired || (!_loaded && !_executing) || !invocation.IsActive) return false;
        if (invocation.Callback.Type != DataType.Function || !ReferenceEquals(invocation.Callback.Function.OwnerScript, _script))
            throw new InvalidOperationException("The event callback belongs to another script.");
        return Enqueue(null, Array.Empty<object?>(), invocation);
    }

    // Safe from a source's foreign thread: touch only host-owned cancellation state.
    internal void SignalRetirement()
    {
        Interlocked.Exchange(ref _retirementRequested, 1);
        Volatile.Read(ref _activeBudget)?.Cancel();
    }

    private bool QueueMessage(Func<IQueuedScriptInvocation> createInvocation, int bytes)
    {
        EnsureOwner();
        ApplyRetirementRequest();
        if (_retired || !_loaded) return false;
        // Check both bounds before snapshotting host data or constructing Lua values.
        if (bytes < 0 || bytes > 64 * 1024 || _events.Count >= 64 || _queuedMessageBytes > 64 * 1024 - bytes)
        {
            Retire();
            Report("Script message queue exceeded its limit; session retired.");
            return false;
        }
        try
        {
            using var scope = _memory.EnterScope();
            using var reservation = MemoryBudget.Reserve(4096L + bytes * 8L);
            var invocation = createInvocation();
            var queued = MemoryBudget.Allocate(512, () => new QueuedScriptEvent(null, Array.Empty<object?>(), invocation, bytes));
            reservation.Bind(invocation);
            _events.Enqueue(queued);
            _queuedMessageBytes += bytes;
            return true;
        }
        catch (ScriptExecutionAbortedException)
        {
            Retire();
            Report("Script allocation allowance exhausted while queuing a message; session retired.");
            return false;
        }
    }

    private bool Enqueue(string? function, object?[] arguments, IQueuedScriptInvocation? callback)
    {
        if (_events.Count >= 64)
        {
            Retire();
            Report("Script event queue exceeded its limit; session retired.");
            return false;
        }
        QueuedScriptEvent queued;
        try
        {
            using var memoryScope = _memory.EnterScope();
            var copy = arguments.Length == 0 ? Array.Empty<object?>() :
                MemoryBudget.Allocate(32L + 8L * arguments.Length, () => (object?[])arguments.Clone());
            for (var index = 0; index < copy.Length; index++)
                if (copy[index] is string text)
                {
                    if (text.Length > ScriptBindingRegistry.MaxStringBytes ||
                        Encoding.UTF8.GetByteCount(text) > ScriptBindingRegistry.MaxStringBytes)
                        throw new ArgumentException("Queued host strings exceed their byte limit.", nameof(arguments));
                    copy[index] = MemoryBudget.AllocateString(text.Length, () => new string(text.AsSpan()));
                }
            queued = MemoryBudget.Allocate(512L + 32L * arguments.Length,
                () => new QueuedScriptEvent(function, copy, callback));
        }
        catch (ScriptExecutionAbortedException)
        {
            Retire();
            Report("Script allocation allowance exhausted while queuing an event; session retired.");
            return false;
        }
        _events.Enqueue(queued);
        return true;
    }

    internal bool DispatchQueuedEvent(TimeSpan timeout)
    {
        EnsureOwner();
        ApplyRetirementRequest();
        if (_retired || _events.Count == 0) return false;
        var queued = _events.Dequeue();
        _queuedMessageBytes -= queued.MessageBytes;
        if (queued.Callback != null)
        {
            if (!queued.Callback.IsActive) return true;
            var callbackResult = Execute(() => queued.Callback.Callback,
                () => queued.Callback.ResolveArguments(_script!), timeout, discardResult: true);
            if (!callbackResult.Succeeded) Report(callbackResult.Message);
            return true;
        }
        // Optional handlers are allowed. Looking up a global does not execute Lua.
        if (_script!.Globals.Get(queued.Function!).Type != DataType.Function) return true;
        var result = InvokeWithin(queued.Function!, queued.Arguments, timeout);
        if (!result.Succeeded) Report(result.Message);
        return true;
    }

    private ScriptExecutionResult Execute(Func<DynValue> resolveFunction, Func<DynValue[]>? resolveArguments = null,
        TimeSpan? timeout = null, bool discardResult = false)
    {
        if (_executing) throw new InvalidOperationException("Synchronous script reentry is disabled; queue callbacks instead.");
        ApplyRetirementRequest();
        if (_retired) return Retired();
        _executing = true;
        MemoryBudget.AllocationReservation failureMemory = default;
        try
        {
            using var memoryScope = _memory.EnterScope();
            // Error reporting must remain bounded and usable after a terminal
            // allowance failure. The ticket is cancelled on successful entries.
            failureMemory = MemoryBudget.Reserve(4096);
            var duration = timeout.HasValue && timeout.Value < _options.Timeout ? timeout.Value : _options.Timeout;
            var budget = MemoryBudget.Allocate(192, () => new ExecutionBudget(_options.InstructionLimit, duration, _options.MaxCallDepth));
            _script!.ExecutionBudget = budget;
            Volatile.Write(ref _activeBudget, budget);
            _memory.Sweep();
            budget.Check();
            if (Volatile.Read(ref _retirementRequested) != 0) budget.Cancel();
            var function = resolveFunction();
            var arguments = resolveArguments?.Invoke() ?? Array.Empty<DynValue>();
            budget.Check();
            var coroutine = _script.CreateCoroutine(function).Coroutine;
            coroutine.AutoYieldCounter = _options.InstructionSlice;
            var result = coroutine.Resume(arguments);
            while (result.Type == DataType.YieldRequest)
            {
                budget.Check();
                result = coroutine.Resume();
            }
            budget.Check();
            return discardResult ? CompletedResult : ConvertResult(result);
        }
        catch (ScriptExecutionAbortedException exception)
        {
            Retire();
            return TerminatedResults[(int)exception.Reason];
        }
        catch (InterpreterException exception)
        {
            Retire();
            var message = exception.DecoratedMessage ?? exception.Message;
            var copy = new string(message.AsSpan(0, Math.Min(message.Length, 768)));
            var result = new ScriptExecutionResult(ScriptExecutionStatus.ScriptError, copy);
            failureMemory.Bind(copy.Length == 0 ? result : copy);
            return result;
        }
        catch (Exception exception) when (exception is OutOfMemoryException or StackOverflowException)
        {
            // Do not disguise a fatal CLR allocation/stack failure as containment.
            // Signal without allocating cleanup structures; any later owner entry
            // tears down before it can parse, convert values, or execute Lua again.
            SignalRetirement();
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && exception is not StackOverflowException)
        {
            // Host failures may leave interpreter frames partially unwound. Retire,
            // rather than recovering and continuing the same interpreter instance.
            Retire();
            Report("Script host failure; session retired.");
            return HostFailureResult;
        }
        finally
        {
            _executing = false;
            failureMemory.Dispose();
            Volatile.Write(ref _activeBudget, null);
            // A completed entry point gets a fresh budget on its next invocation.
            // Retired VMs retain their cancelled budget even if a descriptor survives.
            if (_script != null) _script.ExecutionBudget = null;
        }
    }

    private ScriptExecutionResult ConvertResult(DynValue value)
    {
        object? result = value.Type switch
        {
            DataType.Nil or DataType.Void => null,
            DataType.Boolean => MemoryBudget.Allocate(32, () => (object)value.Boolean),
            DataType.Number when double.IsFinite(value.Number) => MemoryBudget.Allocate(32, () => (object)value.Number),
            DataType.String when value.String.Length <= _options.MaxResultStringBytes &&
                Encoding.UTF8.GetByteCount(value.String) <= _options.MaxResultStringBytes =>
                MemoryBudget.AllocateString(value.String.Length, () => new string(value.String.AsSpan())),
            _ => null
        };
        if (result == null && value.Type is not DataType.Nil and not DataType.Void)
        {
            Retire();
            return InvalidResult;
        }
        return MemoryBudget.Allocate(64, () => new ScriptExecutionResult(ScriptExecutionStatus.Completed, "Completed.", result));
    }

    private DynValue[] ConvertArguments(object?[] arguments)
    {
        var converted = MemoryBudget.Allocate(32L + 8L * arguments.Length, () => new DynValue[arguments.Length]);
        for (var index = 0; index < arguments.Length; index++)
            converted[index] = _bindings.ToScriptValue(_script!, arguments[index]);
        return converted;
    }

    private void EnsureAvailable()
    {
        EnsureOwner();
        ApplyRetirementRequest();
        if (_retired) throw new ObjectDisposedException(nameof(ScriptSession));
    }

    private void ApplyRetirementRequest()
    {
        if (Volatile.Read(ref _retirementRequested) != 0 || _memory.IsTerminated) Retire();
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Script sessions must be accessed on their owner thread.");
    }

    private static ScriptExecutionResult Retired() => RetiredResult;

    private void Retire()
    {
        if (_retired) return;
        _retired = true;
        _events.Clear();
        _queuedMessageBytes = 0;
        if (_script != null)
        {
            _script.ExecutionBudget ??= _retiredBudget;
            _script.ExecutionBudget.Cancel();
            _script.Globals.Clear();
            _script = null;
        }
        for (var i = _resources.Count - 1; i >= 0; i--)
        {
            try { _resources[i].Dispose(); }
            catch (Exception) { Report("Script resource cleanup failed."); }
        }
        _resources.Clear();
        // The capability retains its owner VM. Clearing the resource list alone
        // is insufficient when a host still holds this retired session wrapper.
        _messages = null;
    }

    private void Report(string message)
    {
        // Diagnostics are an observer; a failing logger must not interrupt teardown.
        try { _diagnostic?.Invoke(message); }
        catch (Exception) { }
    }

    public void Dispose()
    {
        EnsureOwner();
        Retire();
    }

    private sealed class RejectingLoader : IScriptLoader
    {
        public object LoadFile(string file, Table globalContext) => throw Denied();
        public string ResolveFileName(string filename, Table globalContext) => throw Denied();
        public string ResolveModuleName(string modname, Table globalContext) => throw Denied();
        private static ScriptRuntimeException Denied() => new("External script and module loading is disabled.");
    }

    private sealed record QueuedScriptEvent(string? Function, object?[] Arguments, IQueuedScriptInvocation? Callback, int MessageBytes = 0);
}
