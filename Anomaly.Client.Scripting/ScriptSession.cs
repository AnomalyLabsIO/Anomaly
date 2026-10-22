using System.Text;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Events;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Loaders;
using MoonSharp.Interpreter.Platforms;

namespace Anomaly.Client.Scripting;

/// <summary>
/// Local development/test boundary only. All entry points are internal; remote source
/// is rejected before construction. This class deliberately claims no heap quota.
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
    private readonly Action<string>? _diagnostic;
    private readonly List<IDisposable> _resources = new();
    private readonly Queue<(string? Function, object?[] Arguments, ScriptEventInvocation? Callback)> _events = new();
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private Script? _script;
    private bool _loaded;
    private bool _executing;
    private bool _retired;
    private int _retirementRequested;
    private ExecutionBudget? _activeBudget;

    internal ScriptSession(ScriptBindingRegistry bindings, ScriptExecutionOptions options, Action<string>? diagnostic,
        ScriptEventCatalog? eventCatalog = null)
    {
        options.Validate();
        _bindings = bindings;
        _options = options;
        _diagnostic = diagnostic;

        // These globals belong to the privately named fork, not another mod's engine.
        UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;
        Script.GlobalOptions.Platform = new LimitedPlatformAccessor();
        var script = new Script(CoreModules.None);
        script.Options.ScriptLoader = new RejectingLoader();
        script.Globals.RegisterCoreModules(CoreModules.GlobalConsts | CoreModules.TableIterators |
            CoreModules.Basic | CoreModules.Math | CoreModules.Table | CoreModules.String | CoreModules.ErrorHandling);
        script.Globals.Get("string").Table.Remove("dump");
        foreach (var key in script.Globals.Keys.ToArray())
            if (key.Type != DataType.String || !AllowedGlobals.Contains(key.String)) script.Globals.Remove(key);
        _script = script;
        if (eventCatalog != null)
        {
            var subscriptions = new ScriptEventSubscriptions(eventCatalog, script,
                invocation => QueueCallback(invocation), requestRetirement: SignalRetirement);
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

    internal void SetGlobal(string name, object? value)
    {
        EnsureAvailable();
        if (_executing) throw new InvalidOperationException("Cannot change script globals during execution.");
        if (string.IsNullOrEmpty(name) || name.Length > 64 || name.StartsWith('_') || char.IsDigit(name[0]) ||
            !name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_') ||
            AllowedGlobals.Contains(name) || name is "Events" or "io" or "os" or "package" or "debug" or "require" or "load" or "loadfile" or "dofile")
            throw new ArgumentException("A host global must use a non-reserved simple name.", nameof(name));
        _bindings.SetGlobal(_script!, name, value);
    }

    internal void TrackResource(IDisposable resource)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(resource);
        _resources.Add(resource);
    }

    internal ScriptExecutionResult Load(string? source)
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
        var result = Execute(() => _script!.LoadString(source!, codeFriendlyName: "anomaly-script"));
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
        }, () => arguments.Select(value => _bindings.ToScriptValue(_script!, value)).ToArray(), timeout);
    }

    // C# events enqueue data only. No CLR event or Lua delegate is exposed to scripts.
    internal bool QueueEvent(string functionName, params object?[] arguments)
    {
        EnsureOwner();
        ApplyRetirementRequest();
        if (_retired || !_loaded) return false;
        if (string.IsNullOrEmpty(functionName) || functionName.Length > 64 || arguments == null || arguments.Length > 16)
            throw new ArgumentException("Invalid queued script event.");
        return Enqueue(functionName, (object?[])arguments.Clone(), null);
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

    private bool Enqueue(string? function, object?[] arguments, ScriptEventInvocation? callback)
    {
        if (_events.Count >= 64)
        {
            Retire();
            Report("Script event queue exceeded its limit; session retired.");
            return false;
        }
        _events.Enqueue((function, arguments, callback));
        return true;
    }

    internal bool DispatchQueuedEvent(TimeSpan timeout)
    {
        EnsureOwner();
        ApplyRetirementRequest();
        if (_retired || _events.Count == 0) return false;
        var queued = _events.Dequeue();
        if (queued.Callback != null)
        {
            if (!queued.Callback.IsActive) return true;
            var callbackResult = Execute(() => queued.Callback.Callback,
                () => queued.Callback.ResolveArguments(_script!), timeout, discardResult: true);
            if (!callbackResult.Succeeded) Report(queued.Callback.Name + ": " + callbackResult.Message);
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
        try
        {
            var duration = timeout.HasValue && timeout.Value < _options.Timeout ? timeout.Value : _options.Timeout;
            var budget = new ExecutionBudget(_options.InstructionLimit, duration, _options.MaxCallDepth);
            _script!.ExecutionBudget = budget;
            Volatile.Write(ref _activeBudget, budget);
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
            return discardResult ? new ScriptExecutionResult(ScriptExecutionStatus.Completed, "Completed.") : ConvertResult(result);
        }
        catch (ScriptExecutionAbortedException exception)
        {
            var status = exception.Reason switch
            {
                ExecutionTerminationReason.CpuLimit => ScriptExecutionStatus.CpuLimit,
                ExecutionTerminationReason.Deadline => ScriptExecutionStatus.Deadline,
                ExecutionTerminationReason.DepthLimit => ScriptExecutionStatus.DepthLimit,
                _ => ScriptExecutionStatus.Cancelled
            };
            Retire();
            return new ScriptExecutionResult(status, "Script execution terminated: " + exception.Reason);
        }
        catch (InterpreterException exception)
        {
            Retire();
            var message = exception.DecoratedMessage ?? exception.Message;
            return new ScriptExecutionResult(ScriptExecutionStatus.ScriptError, message.Length > 768 ? message[..768] : message);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && exception is not StackOverflowException)
        {
            // Host failures may leave interpreter frames partially unwound. Retire,
            // rather than recovering and continuing the same interpreter instance.
            Retire();
            Report("Script host failure: " + exception.GetType().Name);
            return new ScriptExecutionResult(ScriptExecutionStatus.ScriptError, "The script host rejected an operation.");
        }
        finally
        {
            _executing = false;
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
            DataType.Boolean => value.Boolean,
            DataType.Number when double.IsFinite(value.Number) => value.Number,
            DataType.String when value.String.Length <= _options.MaxResultStringBytes &&
                Encoding.UTF8.GetByteCount(value.String) <= _options.MaxResultStringBytes => value.String,
            _ => null
        };
        if (result == null && value.Type is not DataType.Nil and not DataType.Void)
        {
            Retire();
            return new ScriptExecutionResult(ScriptExecutionStatus.InvalidResult, "Entry points may return only bounded primitive values.");
        }
        return new ScriptExecutionResult(ScriptExecutionStatus.Completed, "Completed.", result);
    }

    private void EnsureAvailable()
    {
        EnsureOwner();
        ApplyRetirementRequest();
        if (_retired) throw new ObjectDisposedException(nameof(ScriptSession));
    }

    private void ApplyRetirementRequest()
    {
        if (Volatile.Read(ref _retirementRequested) != 0) Retire();
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Script sessions must be accessed on their owner thread.");
    }

    private static ScriptExecutionResult Retired() => new(ScriptExecutionStatus.Retired, "The script session has been retired.");

    private void Retire()
    {
        if (_retired) return;
        _retired = true;
        _events.Clear();
        if (_script != null)
        {
            _script.ExecutionBudget ??= new ExecutionBudget(1, TimeSpan.FromMilliseconds(1));
            _script.ExecutionBudget.Cancel();
            _script.Globals.Clear();
            _script = null;
        }
        for (var i = _resources.Count - 1; i >= 0; i--)
        {
            try { _resources[i].Dispose(); }
            catch (Exception exception) { Report("Script resource cleanup failed: " + exception.GetType().Name); }
        }
        _resources.Clear();
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
}
