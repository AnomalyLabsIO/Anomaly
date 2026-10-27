using System.Text;
using Anomaly.Client.Scripting.Remote.Protocol;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Loaders;
using MoonSharp.Interpreter.Platforms;

namespace Anomaly.Client.ScriptWorker;

internal sealed partial class WorkerSession
{
    private static readonly HashSet<string> AllowedGlobals = new(StringComparer.Ordinal)
    {
        "_G", "_VERSION", "assert", "error", "select", "type", "tonumber", "tostring", "ipairs", "pairs", "next",
        "pcall", "xpcall", "math", "table", "string"
    };
    internal static readonly HashSet<string> ReservedGlobals = new(AllowedGlobals.Concat(new[]
    {
        "Game", "Output", "Events", "Messages", "Enums", "io", "os", "package", "debug", "require", "load", "loadfile",
        "dofile", "coroutine", "collectgarbage", "rawget", "rawset", "getmetatable", "setmetatable", "print"
    }), StringComparer.Ordinal);
    private readonly WorkerActor _actor;
    private readonly WorkerCatalog _catalog;
    private readonly MemoryBudget _memory;
    private readonly Dictionary<long, WeakReference<Proxy>> _proxies = new(1024);
    private readonly List<long> _proxyHandles = new(1024);
    private readonly List<long> _pendingRelease = new(1024);
    private readonly Dictionary<string, Registration> _registrations = new(32, StringComparer.Ordinal);
    private Dictionary<string, MessageSchema> _schemas = new(StringComparer.Ordinal);
    private bool _loaded;
    private int _sweepCursor;
    internal Script Script { get; }
    internal string? ProtocolError { get; set; }

    internal WorkerSession(WorkerActor actor, WorkerCatalog catalog, MemoryBudget parent)
    {
        _actor = actor;
        _catalog = catalog;
        _memory = new MemoryBudget(16L * 1024 * 1024, parent, reclaimable: true);
        using var scope = _memory.EnterScope();
        _memory.Track(_proxies, 128 + 1024 * 64);
        _memory.Track(_proxyHandles, 64 + 1024 * 8);
        _memory.Track(_pendingRelease, 64 + 1024 * 8);
        _memory.Track(_registrations, 128 + 32 * 128);
        UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;
        MoonSharp.Interpreter.Script.GlobalOptions.Platform = new LimitedPlatformAccessor();
        Script = new Script(CoreModules.None, _memory);
        Script.Options.ScriptLoader = new RejectingLoader();
        Script.Globals.RegisterCoreModules(CoreModules.GlobalConsts | CoreModules.TableIterators | CoreModules.Basic |
            CoreModules.Math | CoreModules.Table | CoreModules.String | CoreModules.ErrorHandling);
        Script.Globals.Get("string").Table.Remove("dump");
        var keys = MemoryBudget.Allocate(8192, () => Script.Globals.Keys.ToArray());
        foreach (var key in keys)
            if (key.Type != DataType.String || !AllowedGlobals.Contains(key.String)) Script.Globals.Remove(key);
        InstallCapabilities();
        if (catalog.Types.ContainsKey("GameContext")) Script.Globals.Set("Game", FromWire(WireValue.Reference(1, "GameContext", 0)));
        Script.Globals.Set("Output", FromWire(WireValue.Reference(2, "ScriptOutput", 0)));
    }

    internal WorkerResult Load(WireValue data)
    {
        Wire.Shape(data, "source", "address", "schemas");
        var source = data.Required("source").RequireString(64 * 1024);
        var address = data.Required("address").RequireString(255);
        if (source.IndexOf('\0') >= 0 || source.IndexOf('\x1b') >= 0 || source.Contains("MoonSharp_dump_b64::", StringComparison.Ordinal) ||
            address.Length == 0 || address.Any(char.IsControl)) throw new InvalidDataException("Only bounded source text is accepted.");
        using (var scope = _memory.EnterScope())
        {
            using var metadata = MemoryBudget.Reserve(512 * 1024);
            _schemas = MessageSchema.Read(data.Required("schemas"));
            metadata.Bind(_schemas);
        }
        var result = Execute(() => Script.LoadString(source, null, address), Array.Empty<WireValue>(), entryBudget: true);
        _loaded = result.Status == "Completed";
        return result;
    }

    internal WorkerResult Invoke(WireValue data, bool named)
    {
        if (!_loaded) throw new InvalidDataException("Session not loaded.");
        Wire.Shape(data, named ? "name" : "token", "args");
        var arguments = data.Required("args").RequireArray(16);
        var name = data.Required(named ? "name" : "token").RequireString(named ? 64 : 32);
        if (named)
            return Execute(() =>
            {
                var function = Script.Globals.Get(name);
                if (function.Type != DataType.Function || !ReferenceEquals(function.Function.OwnerScript, Script))
                    throw new ScriptRuntimeException("The requested entry point is not a script function.");
                return function;
            }, arguments, entryBudget: true);
        if (!_registrations.TryGetValue(name, out var registration)) return WorkerResult.Completed;
        if (arguments.Count != registration.Arity) throw new InvalidDataException("Callback argument count mismatch.");
        if (registration.Arity == 1)
        {
            var value = arguments[0];
            if (value.Kind != WireKind.Object || (registration.Message ? value.Text.Length != 0 : value.Text.Length == 0))
                throw new InvalidDataException("Callback data must be a frozen event or plain message record.");
            var nodes = 0;
            ValidateCallbackData(value, 0, ref nodes, registration.Message);
        }
        return Execute(() => registration.Callback, arguments, discard: true);
    }

    private void ValidateCallbackData(WireValue value, int depth, ref int nodes, bool message)
    {
        if (++nodes > 256 || depth > 8 || value.Kind == WireKind.Reference)
            throw new InvalidDataException("Callback data exceeds its frozen value boundary.");
        if (value.Kind == WireKind.String) value.RequireString(4096);
        if (value.Kind == WireKind.Object)
        {
            if (value.Text.Length != 0 && !_catalog.Types.ContainsKey(value.Text))
                throw new InvalidDataException("Unknown frozen callback type.");
            foreach (var field in value.RequireObject(256))
            {
                if (message && field.Value.Kind is not (WireKind.Boolean or WireKind.Number or WireKind.String))
                    throw new InvalidDataException("Message data must contain flat primitive fields.");
                ValidateCallbackData(field.Value, depth + 1, ref nodes, message);
            }
        }
        if (value.Kind == WireKind.Array)
            foreach (var item in value.RequireArray(256)) ValidateCallbackData(item, depth + 1, ref nodes, message);
    }

    private WorkerResult Execute(Func<DynValue> resolve, IReadOnlyList<WireValue> arguments, bool entryBudget = false, bool discard = false)
    {
        try
        {
            using var scope = _memory.EnterScope();
            _memory.Sweep();
            var budget = MemoryBudget.Allocate(192, () => new ExecutionBudget(entryBudget ? 200000 : 100000,
                TimeSpan.FromMilliseconds(entryBudget ? 100 : 10), 64));
            Script.ExecutionBudget = budget;
            budget.Check();
            var function = resolve();
            var converted = MemoryBudget.Allocate(32L + arguments.Count * 8L, () => new DynValue[arguments.Count]);
            for (var index = 0; index < converted.Length; index++) converted[index] = FromWire(arguments[index]);
            var coroutine = Script.CreateCoroutine(function).Coroutine;
            coroutine.AutoYieldCounter = 2000;
            var result = coroutine.Resume(converted);
            while (result.Type == DataType.YieldRequest) { budget.Check(); result = coroutine.Resume(); }
            budget.Check();
            _memory.Check();
            return discard ? WorkerResult.Completed : new WorkerResult("Completed", ToWirePrimitiveResult(result));
        }
        catch (ScriptExecutionAbortedException exception)
        { return new WorkerResult(exception.Reason.ToString(), null, ProtocolError ?? "Script execution terminated: " + exception.Reason); }
        catch (InterpreterException exception)
        {
            if (Script.ExecutionBudget?.IsTerminated == true)
                return new WorkerResult(Script.ExecutionBudget.TerminationReason.ToString(), null, ProtocolError ?? "Script execution terminated.");
            if (_memory.IsTerminated) return new WorkerResult("MemoryLimit", null, "Script allocation allowance exhausted.");
            return new WorkerResult("ScriptError", null, exception.DecoratedMessage ?? exception.Message);
        }
        catch (OutOfMemoryException) { return new WorkerResult("MemoryLimit", null, "Worker memory limit reached."); }
        finally { Script.ExecutionBudget = null; }
    }

    internal WireValue Rpc(Dictionary<string, WireValue> fields) => _actor.Request(this, fields);
    internal void Check(Script script)
    {
        if (!ReferenceEquals(script, Script)) throw new ScriptRuntimeException("Capability belongs to another session.");
        Script.ExecutionBudget?.Check();
        _memory.Check();
    }

    private WireValue ToWirePrimitiveResult(DynValue result)
    {
        if (result.Type == DataType.Tuple)
        {
            if (result.Tuple.Length > 16) throw new ScriptRuntimeException("Too many entry results.");
            return WireValue.Array(result.Tuple.Select(ToWireScalar).ToArray());
        }
        return ToWireScalar(result);
    }

    private WireValue ToWireScalar(DynValue value)
    {
        switch (value.Type)
        {
            case DataType.Nil:
            case DataType.Void: return WireValue.Null;
            case DataType.Boolean: return WireValue.Flag(value.Boolean);
            case DataType.Number when double.IsFinite(value.Number): return WireValue.Numeric(value.Number);
            case DataType.String when value.String.Length <= 4096 && RemoteProtocol.Utf8.GetByteCount(value.String) <= 4096:
                return WireValue.String(value.String);
            default: throw new ScriptRuntimeException("Only bounded primitive values are accepted.");
        }
    }

    internal WireValue CollectReleases()
    {
        // Only this isolated process is collected; the game process is never touched.
        if (_proxies.Count >= 896) GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        var inspected = Math.Min(64, _proxyHandles.Count);
        for (var i = 0; i < inspected && _proxyHandles.Count != 0; i++)
        {
            if (_sweepCursor >= _proxyHandles.Count) _sweepCursor = 0;
            var key = _proxyHandles[_sweepCursor];
            if (key > 2 && !_proxies[key].TryGetTarget(out _))
            {
                _proxies.Remove(key);
                _proxyHandles[_sweepCursor] = _proxyHandles[^1];
                _proxyHandles.RemoveAt(_proxyHandles.Count - 1);
                _pendingRelease.Add(key);
            }
            else _sweepCursor++;
        }
        var count = Math.Min(64, _pendingRelease.Count);
        var release = MemoryBudget.Allocate(32L + count * 8L, () => new WireValue[count]);
        for (var i = 0; i < count; i++) release[i] = WireValue.Numeric(_pendingRelease[i]);
        _pendingRelease.RemoveRange(0, count);
        return WireValue.Array(release);
    }

    internal static bool IsIdentifier(string name) => name.Length is > 0 and <= 128 &&
        (name[0] is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_') &&
        name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    private sealed class RejectingLoader : IScriptLoader
    {
        public object LoadFile(string file, Table globalContext) => throw Denied();
        public string ResolveFileName(string filename, Table globalContext) => throw Denied();
        public string ResolveModuleName(string modname, Table globalContext) => throw Denied();
        private static ScriptRuntimeException Denied() => new("External script and module loading is disabled.");
    }

    private sealed record Registration(string Token, string Source, DynValue Callback, int Arity, bool Message);
}
