using System.Diagnostics;
using System.Text;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.CoreLib;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Loaders;
using MoonSharp.Interpreter.Platforms;

namespace Anomaly.ScriptSandboxPrototype;

internal sealed class MoonSharpRuntime : IScriptRuntime
{
    public RuntimeExecution Execute(string source, SandboxedObjectApi api, SandboxLimits limits)
    {
        if (source is null || Encoding.UTF8.GetByteCount(source) > limits.MaxSourceBytes)
            return new RuntimeExecution("SourceRejected", "Source exceeds the package byte limit.");

        // LoadString accepts MoonSharp's Base64 binary dumps as well as Lua source.
        if (source.StartsWith(StringModule.BASE64_DUMP_HEADER, StringComparison.Ordinal)
            || source.IndexOf('\0') >= 0 || source.StartsWith('\u001b'))
            return new RuntimeExecution("SourceRejected", "Only Lua source text is accepted; binary dumps are disabled.");

        var stopwatch = Stopwatch.StartNew();
        var resumes = 0;

        try
        {
            // These settings are global to the interpreter assembly. The prototype runs
            // one runtime in a disposable child process, with no other script hosts.
            // In 2.0.0, Default is the non-obsolete name of Explicit; it does not
            // automatically register CLR types. No userdata types are registered here.
            UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;
            Script.GlobalOptions.Platform = new LimitedPlatformAccessor();

            var script = new Script(CoreModules.None);
            script.Options.ScriptLoader = new RejectingScriptLoader();
            script.Globals.RegisterCoreModules(CoreModules.GlobalConsts | CoreModules.TableIterators
                | CoreModules.Basic | CoreModules.Math | CoreModules.Table | CoreModules.String
                | CoreModules.ErrorHandling);
            script.Globals.Remove("print");
            script.Globals.Remove("collectgarbage");
            script.Globals.Get("string").Table.Remove("dump");

            // Raw CLR/Unity objects and automatic conversion are never exposed.
            // Charge rejected signatures too, before inspecting or converting values.
            var hostAttempts = 0;
            void ChargeAttempt()
            {
                if (hostAttempts >= limits.MaxCommands)
                    throw new ScriptRuntimeException("objects host-call attempt budget exceeded.");
                hostAttempts++;
            }

            var objects = new Table(script);
            objects.Set("spawn", Bind("spawn", 1, ChargeAttempt, args =>
                DynValue.NewString(api.Spawn(StringArgument(args, 0, "spawn")))));
            objects.Set("set_position", Bind("set_position", 4, ChargeAttempt, args =>
            {
                api.SetPosition(StringArgument(args, 0, "set_position"),
                    NumberArgument(args, 1, "set_position"),
                    NumberArgument(args, 2, "set_position"),
                    NumberArgument(args, 3, "set_position"));
                return DynValue.Nil;
            }));
            objects.Set("set_label", Bind("set_label", 2, ChargeAttempt, args =>
            {
                api.SetLabel(StringArgument(args, 0, "set_label"), StringArgument(args, 1, "set_label"));
                return DynValue.Nil;
            }));
            objects.Set("destroy", Bind("destroy", 1, ChargeAttempt, args =>
            {
                api.Destroy(StringArgument(args, 0, "destroy"));
                return DynValue.Nil;
            }));
            script.Globals.Set("objects", DynValue.NewTable(objects));

            // RegisterCoreModules creates a package table even without LoadMethods.
            // Retain only this explicit surface, including when a module adds globals.
            var allowedGlobals = new HashSet<string>(StringComparer.Ordinal)
            {
                "_G", "_VERSION", "assert", "error", "select", "type", "tonumber", "tostring",
                "ipairs", "pairs", "next", "pcall", "xpcall", "math", "table", "string", "objects"
            };
            foreach (var key in script.Globals.Keys.ToArray())
            {
                if (key.Type != DataType.String || !allowedGlobals.Contains(key.String))
                    script.Globals.Remove(key);
            }

            // Initialization is untrusted too: DoString/Call would bypass this coroutine's
            // cooperative instruction slices. Parsing itself is covered by the parent watchdog.
            var chunk = script.LoadString(source, codeFriendlyName: "server.lua");
            var coroutine = script.CreateCoroutine(chunk).Coroutine;
            coroutine.AutoYieldCounter = limits.InstructionSlice;

            while (resumes < limits.MaxResumes)
            {
                if (stopwatch.ElapsedMilliseconds >= limits.ExecutionTimeoutMs)
                    return CpuLimit("Host elapsed-time budget exceeded between VM resumes.", resumes);

                resumes++;
                var result = coroutine.Resume();

                if (stopwatch.ElapsedMilliseconds >= limits.ExecutionTimeoutMs)
                    return CpuLimit("Host elapsed-time budget exceeded during a VM resume.", resumes);

                if (result.Type != DataType.YieldRequest)
                    return new RuntimeExecution("Completed", "Lua chunk completed.", Resumes: resumes);
            }

            return CpuLimit("Host instruction-slice resume budget exceeded.", resumes);
        }
        catch (InterpreterException ex)
        {
            var message = ex.DecoratedMessage ?? ex.Message;
            if (message.Length > 768)
                message = message[..768];
            return new RuntimeExecution("ScriptError", message, Resumes: resumes);
        }
        // There is no per-script heap quota here. AutoYieldCounter also cannot interrupt
        // host work or non-yieldable Lua re-entry (for example a string.gsub callback).
        // The parent process must report those watchdog interventions separately.
    }

    private static RuntimeExecution CpuLimit(string message, int resumes)
        => new("CpuLimit", message, Resumes: resumes);

    private static DynValue Bind(string name, int count, Action chargeAttempt, Func<CallbackArguments, DynValue> callback)
    {
        return DynValue.NewCallback((_, args) =>
        {
            chargeAttempt();
            if (args.Count != count)
                throw new ScriptRuntimeException($"objects.{name} requires {count} arguments.");

            try
            {
                return callback(args);
            }
            catch (Exception ex) when (ex is not InterpreterException && ex is not OutOfMemoryException
                && ex is not StackOverflowException)
            {
                throw new ScriptRuntimeException($"objects.{name}: {ex.Message}");
            }
        }, $"objects.{name}");
    }

    private static string StringArgument(CallbackArguments args, int index, string name)
    {
        if (args[index].Type != DataType.String)
            throw new ScriptRuntimeException($"objects.{name} argument {index + 1} must be a string.");
        var value = args[index].String;
        if (value.Length > 4096 || Encoding.UTF8.GetByteCount(value) > 4096)
            throw new ScriptRuntimeException("Host string exceeds 4096 UTF-8 bytes.");
        return value;
    }

    private static double NumberArgument(CallbackArguments args, int index, string name)
    {
        if (args[index].Type != DataType.Number)
            throw new ScriptRuntimeException($"objects.{name} argument {index + 1} must be a number.");
        return args[index].Number;
    }

    // MoonSharp 2.0.0's InvalidScriptLoader has no public constructor. This is its
    // fail-closed equivalent, without reflection or any filesystem/module resolution.
    private sealed class RejectingScriptLoader : IScriptLoader
    {
        public object LoadFile(string file, Table globalContext) => throw Denied();
        public string ResolveFileName(string filename, Table globalContext) => throw Denied();
        public string ResolveModuleName(string modname, Table globalContext) => throw Denied();

        private static ScriptRuntimeException Denied()
            => new("External script and module loading is disabled.");
    }
}
