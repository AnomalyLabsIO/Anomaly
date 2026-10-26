using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.CoreLib;
using MoonSharp.Interpreter.Debugging;

namespace Anomaly.ScriptingCpuProbe;

internal static partial class Program
{
    private static void ExecuteClrAllocations(string fixture, ProbeState state)
    {
        const CoreModules modules = CoreModules.GlobalConsts | CoreModules.TableIterators | CoreModules.Basic |
            CoreModules.Math | CoreModules.Table | CoreModules.String | CoreModules.ErrorHandling;
        switch (fixture)
        {
            case "clralloc-error-format-preflight":
            case "clralloc-error-alignment-preflight":
            {
                var input = new string('x', 512 * 1024);
                state.Memory = new MemoryBudget(64 * 1024);
                using var scope = state.Memory.EnterScope();
                CountClrAllocation(state, () => _ = fixture == "clralloc-error-format-preflight"
                    ? new ScriptRuntimeException("prefix {0}", input)
                    : new ScriptRuntimeException("{0,4194304}", "x"));
                break;
            }
            case "clralloc-exception-preflight":
            {
                state.Memory = new MemoryBudget(4096);
                using var scope = state.Memory.EnterScope();
                _ = new ScriptRuntimeException("bounded ordinary Lua failure");
                break;
            }
            case "clralloc-randomseed-preflight":
            {
                state.Memory = new MemoryBudget(16L * 1024 * 1024);
                var script = new Script(modules, state.Memory);
                FreshCpuBudget(script, state);
                var callback = DynValue.NewCallback((context, arguments) =>
                {
                    state.HostCalls++;
                    LeaveMemory(state, 512);
                    return MathModule.randomseed(context, arguments);
                });
                CountClrAllocation(state, () => script.Call(callback, DynValue.NewNumber(7)));
                break;
            }
            case "clralloc-tonumber-no-trim-copy":
            {
                var input = " " + new string('0', 512 * 1024) + "1 ";
                state.Memory = new MemoryBudget(16L * 1024 * 1024);
                var script = new Script(modules, state.Memory);
                FreshCpuBudget(script, state);
                var value = DynValue.NewString(input);
                var radix = DynValue.NewNumber(2);
                var function = script.Globals.Get("tonumber");
                LeaveMemory(state, 64 * 1024);
                CountClrAllocation(state, () => state.Number = script.Call(function, value, radix).Number);
                break;
            }
            case "clralloc-location-preflight":
            {
                var name = new string('n', 512 * 1024);
                state.Memory = new MemoryBudget(16L * 1024 * 1024);
                var script = new Script(CoreModules.None, state.Memory);
                FreshCpuBudget(script, state);
                script.LoadString("return 1", null, name);
                var location = new SourceRef(0, 0, 1, 1, 1, false);
                LeaveMemory(state, 32 * 1024);
                CountClrAllocation(state, () => _ = location.FormatLocation(script));
                break;
            }
            case "clralloc-tostring-preflight":
            case "clralloc-tuple-print-preflight":
            {
                var text = DynValue.NewString(new string('x', 512 * 1024));
                var value = fixture == "clralloc-tostring-preflight" ? text : DynValue.NewTuple(text, text);
                state.Memory = new MemoryBudget(64 * 1024);
                using var scope = state.Memory.EnterScope();
                CountClrAllocation(state, () => _ = fixture == "clralloc-tostring-preflight"
                    ? value.ToString() : value.ToPrintString());
                break;
            }
            case "clralloc-error-loop-reclaim":
            {
                state.Memory = new MemoryBudget(16L * 1024 * 1024, reclaimable: true);
                var script = new Script(modules, state.Memory);
                FreshCpuBudget(script, state);
                var function = script.LoadString("local ok = pcall(function() error('fixture',0) end); return ok");
                ForceReclaim(state.Memory, state);
                var baseline = state.Memory.ChargedBytes;
                for (var index = 0; index < 1024; index++)
                {
                    FreshCpuBudget(script, state);
                    ReclaimCheck(state, !script.Call(function).Boolean, "Ordinary errors stopped being catchable.");
                    state.Entries++;
                    if ((index + 1) % 32 == 0) ForceReclaim(state.Memory, state);
                }
                ForceReclaim(state.Memory, state);
                ReclaimCheck(state, state.Memory.ChargedBytes <= baseline + 128 * 1024,
                    "Discarded ordinary diagnostics retained cumulative allocation credit.");
                ReclaimCheck(state, state.Memory.TotalAllocatedBytes > baseline + 8L * 1024 * 1024,
                    "Ordinary CLR error allocations were not included in accounting.");
                GC.KeepAlive(script);
                break;
            }
            case "clralloc-list-capacity-preflight":
            {
                // Exercise the actual compiler/bytecode container with a bounded
                // trusted input array, so a regression requests an 8 MiB CLR
                // backing array without involving parser source estimates.
                var values = new object[1024 * 1024];
                var type = typeof(Script).Assembly.GetType("MoonSharp.Interpreter.BudgetedList`1", true)!
                    .MakeGenericType(typeof(object));
                var add = type.GetMethod("AddRange", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
                state.Memory = new MemoryBudget(64 * 1024);
                using var scope = state.Memory.EnterScope();
                var list = Activator.CreateInstance(type, nonPublic: true)!;
                var arguments = new object[] { values };
                CountClrAllocation(state, () => InvokeClrFixture(add, list, arguments));
                break;
            }
            case "clralloc-dictionary-capacity-preflight":
            case "clralloc-hashset-capacity-preflight":
            {
                var dictionary = fixture == "clralloc-dictionary-capacity-preflight";
                var definition = dictionary ? "MoonSharp.Interpreter.BudgetedDictionary`2" : "MoonSharp.Interpreter.BudgetedHashSet`1";
                var type = typeof(Script).Assembly.GetType(definition, true)!
                    .MakeGenericType(dictionary ? new[] { typeof(object), typeof(object) } : new[] { typeof(object) });
                var add = type.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
                state.Memory = new MemoryBudget(64 * 1024);
                using var scope = state.Memory.EnterScope();
                var container = Activator.CreateInstance(type, nonPublic: true)!;
                var arguments = dictionary ? new[] { new object(), new object() } : new[] { new object() };
                LeaveMemory(state, 1);
                CountClrAllocation(state, () => InvokeClrFixture(add, container, arguments));
                break;
            }
            default: throw new ArgumentException("Unknown fixed CLR allocation fixture.", nameof(fixture));
        }
    }

    private static void CountClrAllocation(ProbeState state, Action operation)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        try { operation(); }
        finally { state.AllocationDeltaBytes = GC.GetAllocatedBytesForCurrentThread() - before; }
    }

    private static void InvokeClrFixture(MethodInfo method, object target, object[] arguments)
    {
        try { method.Invoke(target, arguments); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
}
