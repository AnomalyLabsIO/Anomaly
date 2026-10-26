using MoonSharp.Interpreter;

namespace Anomaly.ScriptingCpuProbe;

internal static partial class Program
{
    private static void ExecuteBuiltinWork(string fixture, ProbeState state)
    {
        const CoreModules modules = CoreModules.GlobalConsts | CoreModules.TableIterators | CoreModules.Basic |
            CoreModules.Math | CoreModules.Table | CoreModules.String | CoreModules.ErrorHandling;
        var script = new Script(modules);
        FreshCpuBudget(script, state);
        string source;
        switch (fixture)
        {
            case "cpuwork-number-coercion":
                script.Globals.Set("text", DynValue.NewString(new string(' ', 128 * 1024) + "1"));
                source = "return text + 0";
                break;
            case "cpuwork-prime-next":
            case "cpuwork-prime-length":
            case "cpuwork-prime-collect":
                script.DoString("t=${}");
                var table = script.Globals.Get("t").Table;
                for (var index = 1; index <= 4096; index++) table.Set(index, DynValue.NewNumber(index));
                if (fixture == "cpuwork-prime-next")
                    for (var index = 1; index < 4096; index++) table.Set(index, DynValue.Nil);
                else if (fixture == "cpuwork-prime-collect")
                    table.Set(4096, DynValue.Nil);
                source = fixture switch
                {
                    "cpuwork-prime-next" => "return next(t)",
                    "cpuwork-prime-length" => "return #t",
                    _ => "t[4097]=1; return 1"
                };
                break;
            case "cpuwork-key-read":
            case "cpuwork-key-write":
            case "cpuwork-key-insert":
                script.Globals.Set("key", DynValue.NewString(new string('k', 128 * 1024)));
                script.DoString("t={}; t[key]=1");
                source = fixture switch
                {
                    "cpuwork-key-write" => "t[key]=2; return 2",
                    "cpuwork-key-insert" => "local empty={}; empty[key]=2; return 2",
                    _ => "return t[key]"
                };
                break;
            case "cpuwork-bounded":
                script.DoString("t=${}; t[1]=1; t[2]=2; t.label=3");
                source = "t.label=t.label+1; local key,value=next(t); " +
                    "return tonumber(' 10 ',2) + (' 3 '+0) + #t + t.label + key + value";
                break;
            default:
                throw new ArgumentException("Unknown fixed built-in work fixture.", nameof(fixture));
        }
        var body = script.LoadString(source);
        // The retained table/input outlives its setup budget. Work must use this
        // entry's budget, never the budget present when the value was created.
        state.Budget!.Cancel();
        state.Budget = new ExecutionBudget(512, TimeSpan.FromSeconds(2), 128);
        script.ExecutionBudget = state.Budget;
        state.Number = script.Call(body).Number;
    }
}
