using MoonSharp.Interpreter;

namespace Anomaly.ScriptingCpuProbe;

internal static partial class Program
{
    // Keep each fixture finite even if instrumentation regresses. The parent also
    // enforces independent process limits, whose intervention always fails a test.
    private static void ExecuteMemory(string fixture, ProbeState state)
    {
        const CoreModules modules = CoreModules.GlobalConsts | CoreModules.TableIterators | CoreModules.Basic |
            CoreModules.Math | CoreModules.Table | CoreModules.String | CoreModules.ErrorHandling;
        if (fixture == "memory-script-initialization")
        {
            state.Memory = new MemoryBudget(1);
            _ = new Script(modules, state.Memory);
            return;
        }

        state.Memory = new MemoryBudget(16L * 1024 * 1024);
        var script = new Script(fixture == "memory-lua-coroutines" ? modules | CoreModules.Coroutine : modules, state.Memory);
        FreshCpuBudget(script, state);
        if (fixture == "memory-bounded")
        {
            state.Number = script.DoString("local t={}; for i=1,100 do t[i]=i end; return #string.rep('x',256) + t[100]").Number;
            return;
        }

        switch (fixture)
        {
            case "memory-preallocation-rep":
            case "memory-preallocation-unpack":
            {
                var operation = fixture == "memory-preallocation-rep"
                    ? "string.rep('x',8388608)" : "table.unpack({},1,8388608)";
                script.DoString("function work() return " + operation + " end");
                var function = script.Globals.Get("work");
                LeaveMemory(state, 512 * 1024);
                var before = GC.GetAllocatedBytesForCurrentThread();
                try { script.Call(function); }
                finally { state.AllocationDeltaBytes = GC.GetAllocatedBytesForCurrentThread() - before; }
                break;
            }
            case "memory-large-rep":
                LeaveMemory(state, 512 * 1024);
                script.DoString("return string.rep('x', 8388608)");
                break;
            case "memory-repeated-format":
                LeaveMemory(state, 512 * 1024);
                // Lua rejects widths longer than two digits; repeated valid,
                // bounded formatting checks cumulative output accounting instead.
                script.DoString("for i=1,5000 do local s=string.format('%99s', 'x') end; return 1");
                break;
            case "memory-pcall-swallow":
                LeaveMemory(state, 512 * 1024);
                script.DoString("local ok = pcall(function() return string.rep('x', 8388608) end); return ok");
                break;
            case "memory-xpcall-swallow":
                LeaveMemory(state, 512 * 1024);
                script.DoString("local ok = xpcall(function() return string.rep('x', 8388608) end, function() return 'caught' end); return ok");
                break;
            case "memory-growing-tables":
                LeaveMemory(state, 512 * 1024);
                script.DoString("local t={}; for i=1,20000 do t[i]={i,i+1,i+2} end; return #t");
                break;
            case "memory-growing-array":
                LeaveMemory(state, 512 * 1024);
                script.DoString("local t={}; for i=1,20000 do t[i]=i end; return #t");
                break;
            case "memory-growing-prime-table":
                LeaveMemory(state, 512 * 1024);
                script.DoString("local t=${}; for i=1,20000 do t[i]=i end; return #t");
                break;
            case "memory-growing-concat":
                LeaveMemory(state, 512 * 1024);
                script.DoString("local s=''; for i=1,10000 do s=s..'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx' end; return #s");
                break;
            case "memory-discarded-strings":
                LeaveMemory(state, 512 * 1024);
                script.DoString("for i=1,2000 do local s=string.rep('x',4096) end; return 1");
                break;
            case "memory-growing-closures":
                LeaveMemory(state, 512 * 1024);
                script.DoString("local t={}; for i=1,10000 do local value=i; t[i]=function() return value end end; return #t");
                break;
            case "memory-table-unpack":
                LeaveMemory(state, 512 * 1024);
                script.DoString("return table.unpack({},1,8388608)");
                break;
            case "memory-table-concat":
                script.DoString("values={}; local s=string.rep('x',1024); for i=1,256 do values[i]=s end; function work() return table.concat(values) end");
                LeaveMemory(state, 64 * 1024);
                script.Call(script.Globals.Get("work"));
                break;
            case "memory-table-sort":
                script.DoString("values={}; for i=1,4096 do values[i]=4097-i end; function work() return table.sort(values) end");
                LeaveMemory(state, 64 * 1024);
                script.Call(script.Globals.Get("work"));
                break;
            case "memory-sort-callback-abort":
                LeaveMemory(state, 512 * 1024);
                script.DoString("return pcall(function() table.sort({3,2,1},function() return #string.rep('x',8388608)==0 end) end)");
                break;
            case "memory-lua-coroutines":
                LeaveMemory(state, 1024 * 1024);
                script.DoString("local t={}; for i=1,4096 do t[i]=coroutine.create(function() return 1 end) end; return #t");
                break;
            case "memory-cumulative-entries":
            {
                script.DoString("function make() return string.rep('x',4096) end");
                var function = script.Globals.Get("make");
                LeaveMemory(state, 1024 * 1024);
                for (var index = 0; index < 1000; index++)
                {
                    FreshCpuBudget(script, state);
                    state.Entries++;
                    state.Number = script.Call(function).String.Length;
                }
                break;
            }
            case "memory-cumulative-coroutines":
            {
                var function = script.LoadString("return 1");
                LeaveMemory(state, 1024 * 1024);
                for (var index = 0; index < 4096; index++)
                {
                    FreshCpuBudget(script, state);
                    state.Resumes++;
                    script.CreateCoroutine(function).Coroutine.Resume();
                }
                break;
            }
            case "memory-source-compilation":
                LeaveMemory(state, 4096);
                script.LoadString("return '" + new string('a', 16000) + "'");
                break;
            case "memory-source-instructions":
                LeaveMemory(state, 128 * 1024);
                script.LoadString(string.Concat(Enumerable.Repeat("value=1;", 4000)));
                break;
            case "memory-host-callback-scope":
            {
                using (state.Memory.EnterScope())
                    script.Globals.Set("payload", DynValue.NewCallback((_, _) =>
                    {
                        state.HostCalls++;
                        // Trusted host code must reserve before making its own CLR allocation.
                        MemoryBudget.ReserveString(8388608);
                        return DynValue.NewString(new string('x', 8388608));
                    }));
                LeaveMemory(state, 512 * 1024);
                script.DoString("return pcall(payload)");
                break;
            }
            case "memory-swallowed-host-abort":
            {
                DynValue callback;
                using (state.Memory.EnterScope())
                    callback = DynValue.NewCallback((_, _) =>
                    {
                        state.HostCalls++;
                        try { MemoryBudget.ReserveString(8388608); }
                        catch (ScriptExecutionAbortedException error) when (error.Reason == ExecutionTerminationReason.MemoryLimit)
                        { state.RejectedCalls++; }
                        // Use an existing singleton: no later allocation should
                        // accidentally substitute for the callback's post-check.
                        return DynValue.True;
                    });
                LeaveMemory(state, 512 * 1024);
                script.Call(callback);
                break;
            }
            case "memory-terminal-repeat-calls":
            {
                var function = script.LoadString("return 42");
                LeaveMemory(state, 512 * 1024);
                try { script.DoString("return string.rep('x',8388608)"); }
                catch (ScriptExecutionAbortedException error) when (error.Reason == ExecutionTerminationReason.MemoryLimit)
                { state.RejectedCalls++; }
                for (var index = 0; index < 3; index++)
                {
                    FreshCpuBudget(script, state);
                    try { script.Call(function); }
                    catch (ScriptExecutionAbortedException error) when (error.Reason == ExecutionTerminationReason.MemoryLimit)
                    { state.RejectedCalls++; }
                }
                if (state.RejectedCalls != 4) throw new InvalidOperationException("A terminated allocation budget was reused.");
                using (state.Memory.EnterScope()) MemoryBudget.Reserve(1);
                break;
            }
            default: throw new ArgumentException("Unknown fixed allocation fixture.", nameof(fixture));
        }
    }

    private static void FreshCpuBudget(Script script, ProbeState state)
    {
        state.Budget = new ExecutionBudget(10_000_000, TimeSpan.FromSeconds(2), 128);
        script.ExecutionBudget = state.Budget;
    }

    private static void LeaveMemory(ProbeState state, long available)
    {
        // Exhaustion after trusted setup lets each probe target one path without
        // depending on the private engine's changing bootstrap accounting cost.
        var bytes = state.Memory!.Limit - state.Memory.ChargedBytes - available;
        if (bytes < 0) throw new InvalidOperationException("Fixture setup exceeded its expected allowance.");
        using (state.Memory.EnterScope()) MemoryBudget.Reserve(bytes);
    }
}
