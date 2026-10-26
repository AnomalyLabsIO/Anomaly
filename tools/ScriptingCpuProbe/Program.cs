using System.Diagnostics;
using System.Text.Json;
using MoonSharp.Interpreter;

namespace Anomaly.ScriptingCpuProbe;

/// <summary>Fixed resource fixtures only. The xUnit parent owns the independent kill watchdog.</summary>
internal static partial class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Main(string[] arguments)
    {
        if (arguments.Length is < 1 or > 2 || (arguments.Length == 2 && arguments[1] != "--wait-for-parent"))
            return 2;
        if (arguments.Length == 2 && Console.ReadLine() != "start")
            return 2;

        var stopwatch = Stopwatch.StartNew();
        var state = new ProbeState();
        try
        {
            // Trusted, finite warm-up removes first-use parser/JIT costs from the
            // deliberately short deadline fixture. It runs no downloaded input.
            new Script(CoreModules.None).DoString("return 1 + 2");
            Execute(arguments[0], state);
            Write(Result("Completed", null, null, state, stopwatch));
            return 0;
        }
        catch (ScriptExecutionAbortedException error)
        {
            Write(Result("Aborted", error.Reason.ToString(), null, state, stopwatch));
            return 0;
        }
        catch (Exception error)
        {
            Write(Result("WrongException", null, error.GetType().FullName + ": " + error.Message, state, stopwatch));
            return 1;
        }
    }

    private static void Write(ProbeResult result) => Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

    private static ProbeResult Result(string outcome, string? reason, string? error, ProbeState state, Stopwatch stopwatch)
        => new(outcome, reason, error, (state.Budget?.IsTerminated ?? false) || (state.Memory?.IsTerminated ?? false),
            state.Resumes, state.HostCalls, state.RejectedCalls, state.Number, stopwatch.ElapsedMilliseconds,
            state.MemoryChargedOverride ?? state.Memory?.ChargedBytes ?? 0, state.MemoryLimitOverride ?? state.Memory?.Limit ?? 0,
            state.Memory?.IsTerminated ?? false, state.Entries,
            state.AllocationDeltaBytes, state.MemoryTotalOverride ?? state.Memory?.TotalAllocatedBytes ?? 0, state.Memory?.PeakBytes ?? 0,
            state.ReleasedRecords, state.Checks, state.ParentChargedBytes, state.MemoryUnboundOverride ?? state.Memory?.UnboundBytes ?? 0);

    private static Script Create(ProbeState state, long fuel = 50_000, int depth = 128, int timeoutMs = 1_000)
    {
        var script = new Script(CoreModules.Preset_SoftSandbox);
        state.Budget = new ExecutionBudget(fuel, TimeSpan.FromMilliseconds(timeoutMs), depth);
        script.ExecutionBudget = state.Budget;
        return script;
    }

    private static void Execute(string fixture, ProbeState state)
    {
        if (fixture.StartsWith("clralloc-", StringComparison.Ordinal))
        {
            ExecuteClrAllocations(fixture, state);
            return;
        }
        if (fixture.StartsWith("hostalloc-", StringComparison.Ordinal))
        {
            ExecuteHostAllocations(fixture, state);
            return;
        }
        if (fixture.StartsWith("reclaimable-", StringComparison.Ordinal))
        {
            ExecuteReclaimable(fixture, state);
            return;
        }
        if (fixture.StartsWith("memory-", StringComparison.Ordinal))
        {
            ExecuteMemory(fixture, state);
            return;
        }
        switch (fixture)
        {
            case "bounded-main":
            {
                var script = Create(state);
                state.Number = script.DoString("local sum = 0; for i=1,100 do sum=sum+i end; return sum").Number;
                break;
            }
            case "main-loop": Create(state).DoString("while true do end"); break;
            case "pcall-loop": Create(state).DoString("pcall(function() while true do end end)"); break;
            case "xpcall-loop": Create(state).DoString("xpcall(function() while true do end end, function() return 'caught' end)"); break;
            case "xpcall-error-handler-loop":
                Create(state).DoString("xpcall(function() error('fixture') end, function() while true do end end)");
                break;
            case "gsub-callback-loop":
                Create(state).DoString("string.gsub('a', '.', function() while true do end end)");
                break;
            case "sort-callback-loop":
                Create(state).DoString("table.sort({3,1,2}, function() while true do end end)");
                break;
            case "clr-callback-reentry-loop":
            {
                var script = Create(state);
                script.Globals.Set("reenter", DynValue.NewCallback((context, args) =>
                {
                    state.HostCalls++;
                    return context.Call(args[0]);
                }));
                script.DoString("reenter(function() while true do end end)");
                break;
            }
            case "swallowed-host-abort":
            {
                var script = Create(state);
                script.Globals.Set("stop", DynValue.NewCallback((_, _) =>
                {
                    state.HostCalls++;
                    state.Budget!.Cancel();
                    state.Budget.Check();
                    return DynValue.Nil;
                }));
                script.DoString("while true do pcall(stop) end");
                break;
            }
            case "coroutine-loop":
            {
                var script = Create(state);
                var coroutine = script.CreateCoroutine(script.LoadString("while true do end")).Coroutine;
                state.Resumes++;
                coroutine.Resume();
                break;
            }
            case "cumulative-resume-fuel":
            {
                var script = Create(state, fuel: 1_000);
                var coroutine = script.CreateCoroutine(script.LoadString("while true do end")).Coroutine;
                coroutine.AutoYieldCounter = 100;
                // Bound the host loop independently: a reset-on-resume regression
                // must fail the result assertion instead of running indefinitely.
                for (var index = 0; index < 100; index++)
                {
                    state.Resumes++;
                    coroutine.Resume();
                }
                break;
            }
            case "cumulative-coroutine-fuel":
            {
                var script = Create(state, fuel: 1_000);
                var body = script.LoadString("while true do end");
                var first = script.CreateCoroutine(body).Coroutine;
                var second = script.CreateCoroutine(body).Coroutine;
                first.AutoYieldCounter = second.AutoYieldCounter = 100;
                for (var index = 0; index < 100; index++)
                {
                    state.Resumes++;
                    (index % 2 == 0 ? first : second).Resume();
                }
                break;
            }
            case "depth-limit":
                Create(state, depth: 16).DoString("function recur() return 1 + recur() end; return recur()");
                break;
            case "source-nesting-depth":
                Create(state, depth: 16).DoString("return " + new string('(', 256) + "1" + new string(')', 256));
                break;
            case "cyclic-call-metamethod":
                Create(state, depth: 16).DoString("local t = {}; setmetatable(t, {__call=t}); return t()");
                break;
            case "nested-clr-pcall-depth":
            {
                // pcall invokes CLR callees directly; this chain must share the
                // depth guard even though it bypasses Lua call-frame recursion.
                var arguments = string.Join(", ", Enumerable.Repeat("pcall", 127));
                Create(state, depth: 16).DoString("return pcall(" + arguments + ", tonumber, '1')");
                break;
            }
            case "builtin-pattern-work":
            {
                var script = Create(state);
                // Host-created bounded input isolates work inside pattern matching
                // from string.rep and compilation; no memory-growth fixture here.
                script.Globals.Set("text", DynValue.NewString(new string('a', 1_024)));
                script.DoString("return string.match(text, '^a*a*a*a*a*a*a*a*b$')");
                break;
            }
            case "expired-deadline":
            {
                var script = Create(state, timeoutMs: 10);
                var function = script.LoadString("return 42");
                Thread.Sleep(25);
                script.Call(function);
                break;
            }
            case "cancelled-lua-call":
            case "cancelled-clr-call":
            {
                var script = Create(state);
                var function = fixture == "cancelled-lua-call"
                    ? script.LoadString("return 42")
                    : DynValue.NewCallback((_, _) => { state.HostCalls++; return DynValue.NewNumber(42); });
                state.Budget!.Cancel();
                script.Call(function);
                break;
            }
            case "deadline-after-clr-return":
            {
                // The host call is deliberately finite. A deadline cannot
                // preempt arbitrary CLR work, but must be checked on return.
                var script = new Script(CoreModules.Preset_SoftSandbox);
                script.Call(DynValue.NewCallback((_, _) => DynValue.Nil));
                var callback = DynValue.NewCallback((_, _) =>
                {
                    state.HostCalls++;
                    Thread.Sleep(50);
                    return DynValue.NewNumber(42);
                });
                state.Budget = new ExecutionBudget(50_000, TimeSpan.FromMilliseconds(20), 128);
                script.ExecutionBudget = state.Budget;
                script.Call(callback);
                break;
            }
            case "terminal-clr-wrapper-catch":
            {
                var script = Create(state);
                var spin = script.LoadString("while true do end");
                var callback = DynValue.NewCallback((context, _) =>
                {
                    state.HostCalls++;
                    try { return context.Call(spin); }
                    catch (ScriptExecutionAbortedException) { return DynValue.NewNumber(42); }
                });
                // Even a trusted wrapper accidentally swallowing the exception
                // must not turn a terminal budget into a successful direct call.
                script.Call(callback);
                break;
            }
            case "terminal-repeat-calls":
            {
                var script = Create(state, fuel: 200);
                var spin = script.LoadString("while true do end");
                var harmless = script.LoadString("return 42");
                try { script.Call(spin); }
                catch (ScriptExecutionAbortedException) { state.RejectedCalls++; }
                var originalReason = state.Budget!.TerminationReason;
                for (var index = 0; index < 3; index++)
                {
                    try { script.Call(harmless); }
                    catch (ScriptExecutionAbortedException error) when (error.Reason == originalReason)
                    { state.RejectedCalls++; }
                }
                if (state.RejectedCalls != 4)
                    throw new InvalidOperationException("A terminated script was reused successfully.");
                state.Budget.Check();
                break;
            }
            default: throw new ArgumentException("Unknown fixed CPU fixture.", nameof(fixture));
        }
    }

    private sealed class ProbeState
    {
        internal ExecutionBudget? Budget;
        internal MemoryBudget? Memory;
        internal int Resumes;
        internal int HostCalls;
        internal int RejectedCalls;
        internal double? Number;
        internal int Entries;
        internal long AllocationDeltaBytes;
        internal int ReleasedRecords;
        internal int Checks;
        internal long ParentChargedBytes;
        internal object? RetainedOwner;
        internal long? MemoryChargedOverride, MemoryLimitOverride, MemoryTotalOverride, MemoryUnboundOverride;
    }

    private sealed record ProbeResult(string Outcome, string? Reason, string? Error, bool Terminal,
        int Resumes, int HostCalls, int RejectedCalls, double? Number, long ElapsedMilliseconds,
        long MemoryChargedBytes, long MemoryLimitBytes, bool MemoryTerminal, int Entries, long AllocationDeltaBytes = 0,
        long TotalAllocatedBytes = 0, long PeakBytes = 0, int ReleasedRecords = 0, int Checks = 0, long ParentChargedBytes = 0,
        long UnboundBytes = 0);
}
