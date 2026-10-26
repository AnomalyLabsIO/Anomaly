using System.Runtime.CompilerServices;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Events;
using MoonSharp.Interpreter;

namespace Anomaly.ScriptingCpuProbe;

internal static partial class Program
{
    private static void ExecuteHostAllocations(string fixture, ProbeState state)
    {
        switch (fixture)
        {
            case "hostalloc-retained-argument-strings":
            {
                state.Memory = new MemoryBudget(2 * 1024 * 1024, reclaimable: true);
                var script = new Script(CoreModules.Preset_SoftSandbox, state.Memory);
                state.Budget = new ExecutionBudget(20_000_000, TimeSpan.FromSeconds(3), 64);
                script.ExecutionBudget = state.Budget;
                var bindings = new ScriptBindingRegistry();
                bindings.RegisterType<HostAllocationTarget>();
                bindings.SetGlobal(script, "target", new HostAllocationTarget(() => state.HostCalls++, bytes => state.AllocationDeltaBytes = bytes));
                script.DoString("for index=1,4096 do target:Remember(string.rep('x',1024)) end");
                throw new InvalidOperationException("Retained argument strings exceeded the fixed fixture without exhausting their allowance.");
            }
            case "hostalloc-bounded-binding-churn":
                HostBindingChurn(state);
                break;
            case "hostalloc-error-message-retention":
                HostDiagnosticRetention(state);
                break;
            case "hostalloc-reentrant-snapshot-getter":
                HostReentrantSnapshot(state);
                break;
            case "hostalloc-uncooperative-clr-body":
            {
                using var runtime = new ScriptRuntime();
                runtime.Bindings.RegisterType<HostAllocationTarget>();
                using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions { Timeout = TimeSpan.FromSeconds(1) });
                var target = new HostAllocationTarget(() => state.HostCalls++, bytes => state.AllocationDeltaBytes = bytes);
                session.SetGlobal("target", target);
                var result = session.Load("return target:AllocateUnaccounted()");
                HostAllocationCheck(result.Succeeded && result.Value is double value && value == 65536,
                    "The fixed CLR allocation fixture did not complete.");
                state.Number = (double)result.Value!;
                state.Checks++;
                RecordHostMemory(session, state, 16L * 1024 * 1024);
                GC.KeepAlive(target);
                break;
            }
            default: throw new ArgumentException("Unknown fixed host-allocation fixture.", nameof(fixture));
        }
    }

    private static void HostBindingChurn(ProbeState state)
    {
        using var runtime = new ScriptRuntime();
        runtime.Bindings.RegisterType<HostAllocationTarget>();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions { Timeout = TimeSpan.FromSeconds(1) });
        session.SetGlobal("target", new HostAllocationTarget(() => state.HostCalls++, bytes => state.AllocationDeltaBytes = bytes));
        HostAllocationCheck(session.Load("function work() target.Label='label'; local f=target.Echo; " +
            "assert(tostring(target)=='[script HostAllocationTarget]'); return f('value',42,true) end").Succeeded,
            "Binding churn source did not load.");
        for (var index = 0; index < 2000; index++)
        {
            var result = session.Invoke("work");
            HostAllocationCheck(result.Succeeded && result.Value is double number && number == 42,
                "A bounded binding entry failed: " + result.Status);
            state.Entries++;
            if ((index + 1) % 32 == 0)
            {
                CollectWorkerGarbage();
                runtime.Tick();
            }
        }
        CollectWorkerGarbage();
        runtime.Tick();
        RecordHostMemory(session, state, 16L * 1024 * 1024);
    }

    private static void HostDiagnosticRetention(ProbeState state)
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions
        { InstructionLimit = 2_000_000, Timeout = TimeSpan.FromSeconds(1) });
        RetainBoundedDiagnostic(session, state);
        CollectWorkerGarbage();
        for (var index = 0; index < 32; index++) runtime.Tick();
        var retained = runtime.MemoryChargedBytes;
        state.RetainedOwner = null;
        CollectWorkerGarbage();
        for (var index = 0; index < 32; index++) runtime.Tick();
        var released = retained - runtime.MemoryChargedBytes;
        state.Number = released;
        RecordHostMemory(session, state, 16L * 1024 * 1024);
        HostAllocationCheck(released >= 4096, "Keeping only a diagnostic string did not retain its prepaid backing allowance: " +
            retained + " -> " + runtime.MemoryChargedBytes + ".");
        state.Checks++;
        HostAllocationCheck(session.MemoryUnboundBytes == 0, "Diagnostic production left an unbound reservation.");
        state.Checks++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RetainBoundedDiagnostic(ScriptSession session, ProbeState state)
    {
        var result = session.Load("error(string.rep('x',65536))");
        HostAllocationCheck(result.Status == ScriptExecutionStatus.ScriptError && result.Message.Length == 768,
            "A script error did not produce a bounded copied diagnostic.");
        // The caller never receives the raw reference in a temporary JIT local;
        // clearing this field must remove the worker's final strong reference.
        state.RetainedOwner = result.Message;
    }

    private static void HostReentrantSnapshot(ProbeState state)
    {
        HostAllocationSignals.Reset(() => state.HostCalls++);
        var bindings = new ScriptBindingRegistry();
        using var catalog = ScriptEventCatalog.Discover(bindings, typeof(HostAllocationSignals).Assembly,
            typeof(HostAllocationSignals).Namespace!);
        using var session = new ScriptSession(bindings, new ScriptExecutionOptions { Timeout = TimeSpan.FromSeconds(1) }, null, catalog);
        HostAllocationCheck(session.Load("Events.HostAllocationSignals.Changed.Add(function() error('must not execute') end)").Succeeded,
            "The reentrant snapshot source did not load.");
        HostAllocationSignals.Publish(new HostAllocationPayload());
        HostAllocationCheck(!session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)) && session.IsRetired,
            "A recursive snapshot publisher did not retire its session.");
        state.Checks++;
        state.RejectedCalls = HostAllocationSignals.Removes;
    }

    private static void RecordHostMemory(ScriptSession session, ProbeState state, long limit)
    {
        state.MemoryChargedOverride = session.MemoryChargedBytes;
        state.MemoryTotalOverride = session.MemoryTotalAllocatedBytes;
        state.MemoryLimitOverride = limit;
        state.MemoryUnboundOverride = session.MemoryUnboundBytes;
    }

    private static void HostAllocationCheck(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

}

[ScriptType]
public sealed class HostAllocationTarget
{
    private readonly Action _called;
    private readonly Action<long> _allocated;
    private readonly List<string> _retained = new(4096);
    private byte[]? _unaccounted;
    private string _label = "label";
    internal HostAllocationTarget(Action called, Action<long> allocated) { _called = called; _allocated = allocated; }
    [ScriptVisible(AllowWrite = true)] public string Label { get => _label; set => _label = value; }
    [ScriptVisible] public void Remember(string value) { _retained.Add(value); _called(); }
    [ScriptVisible] public int Echo(string value, int number, bool enabled) { _called(); return enabled && value == "value" ? number : -1; }
    [ScriptVisible] public int AllocateUnaccounted()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        _unaccounted = new byte[65536]; // Deliberately uncooperative, finite trusted CLR code.
        _allocated(GC.GetAllocatedBytesForCurrentThread() - before);
        _called();
        return _unaccounted.Length;
    }
}

public static class HostAllocationSignals
{
    private static Action<HostAllocationPayload>? _changed;
    private static Action? _read;
    public static int Removes { get; private set; }
    [ScriptEvent] public static event Action<HostAllocationPayload> Changed
    { add => _changed += value; remove { _changed -= value; Removes++; } }
    internal static void Reset(Action read) { _changed = null; _read = read; Removes = 0; }
    public static void Publish(HostAllocationPayload value) => _changed?.Invoke(value);
    public static string Read(HostAllocationPayload value)
    {
        _read!();
        Publish(value);
        return "bounded";
    }
}

[ScriptType]
public sealed class HostAllocationPayload
{
    [ScriptVisible] public string Value => HostAllocationSignals.Read(this);
}
