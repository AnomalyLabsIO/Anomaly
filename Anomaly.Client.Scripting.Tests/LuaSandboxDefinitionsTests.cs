using System.Reflection;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Catalog;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class LuaSandboxDefinitionsTests
{
    [Fact]
    public void Editor_standard_library_has_the_same_globals_and_members_as_a_real_session()
    {
        using var session = new ScriptSession(new ScriptBindingRegistry(), new ScriptExecutionOptions(), null);
        var runtime = (Script)typeof(ScriptSession).GetField("_script", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(session)!;
        var editor = new Script(CoreModules.None);
        editor.DoString(LuaSandboxDefinitions.Text);

        Assert.Equal(Names(runtime.Globals), Names(editor.Globals));
        foreach (var name in new[] { "math", "string", "table" })
        {
            var actual = runtime.Globals.Get(name).Table;
            var declared = editor.Globals.Get(name).Table;
            // Numeric math constants are described as class fields rather than assigned values.
            var constants = name == "math" ? new[] { "huge", "pi" } : Array.Empty<string>();
            Assert.Equal(Names(actual).Except(constants).ToArray(), Names(declared));
        }
    }

    [Fact]
    public void Standard_library_definitions_do_not_advertise_functions_removed_from_the_sandbox()
    {
        var editor = new Script(CoreModules.None);
        editor.DoString(LuaSandboxDefinitions.Text);
        foreach (var name in new[] { "io", "os", "package", "debug", "require", "load", "loadfile", "dofile",
                     "print", "collectgarbage", "coroutine", "bit32", "rawget", "rawset", "getmetatable", "setmetatable" })
            Assert.True(editor.Globals.Get(name).IsNil(), name);
        Assert.True(editor.Globals.Get("string").Table.Get("dump").IsNil());
        Assert.True(editor.Globals.Get("math").Table.Get("log10").IsNil());
        Assert.True(editor.Globals.Get("table").Table.Get("move").IsNil());
    }

    private static string[] Names(Table table) => table.Keys.Select(key => key.String)
        .OrderBy(name => name, StringComparer.Ordinal).ToArray();
}
