using System.Text.Json;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Catalog;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class LuaDefinitionGeneratorTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "AnomalyLuaDefinitions", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Regeneration_updates_references_without_replacing_developer_configuration()
    {
        Directory.CreateDirectory(_workspace);
        var config = Path.Combine(_workspace, ".luarc.json");
        var custom = "{\"workspace.library\":[\"my-library\"],\"diagnostics.globals\":[\"MyGlobal\"]}";
        File.WriteAllText(config, custom);
        var catalog = new ScriptBindingRegistry().GetCatalog();
        var first = LuaDefinitionGenerator.Generate(catalog, _workspace, "14.2.7", "1.0");
        var oldReferences = File.ReadAllText(first.DefinitionsPath);
        var next = LuaDefinitionGenerator.Generate(catalog, _workspace, "14.3.0", "1.1");

        Assert.True(next.ConfigurationPreserved);
        Assert.Equal(custom, File.ReadAllText(config));
        Assert.NotEqual(oldReferences, File.ReadAllText(next.DefinitionsPath));
        new Script(CoreModules.None).LoadString(File.ReadAllText(next.DefinitionsPath));
    }

    [Fact]
    public void Force_replaces_existing_configuration_with_a_usable_Lua52_library_workspace()
    {
        Directory.CreateDirectory(_workspace);
        File.WriteAllText(Path.Combine(_workspace, ".luarc.json"), "developer configuration");
        var result = LuaDefinitionGenerator.Generate(new ScriptBindingRegistry().GetCatalog(), _workspace,
            "14.2.7", "1.0", force: true);

        Assert.False(result.ConfigurationPreserved);
        using var settings = JsonDocument.Parse(File.ReadAllText(result.ConfigurationPath));
        Assert.Equal("Lua 5.2", settings.RootElement.GetProperty("runtime.version").GetString());
        var builtins = settings.RootElement.GetProperty("runtime.builtin");
        Assert.Equal("enable", builtins.GetProperty("builtin").GetString());
        foreach (var entry in builtins.EnumerateObject().Where(entry => entry.Name != "builtin"))
            Assert.Equal("disable", entry.Value.GetString());
        var library = settings.RootElement.GetProperty("workspace.library")[0].GetString()!;
        Assert.Equal(Path.GetDirectoryName(result.DefinitionsPath), Path.GetFullPath(library, _workspace));
    }

    [Fact]
    public void Identical_regeneration_does_not_write_readonly_references_but_repairs_changed_body()
    {
        var catalog = new ScriptBindingRegistry().GetCatalog();
        var result = LuaDefinitionGenerator.Generate(catalog, _workspace, "14.2.7", "1.0");
        var expected = File.ReadAllText(result.DefinitionsPath);
        File.SetAttributes(result.DefinitionsPath, FileAttributes.ReadOnly);
        try
        {
            LuaDefinitionGenerator.Generate(catalog, _workspace, "14.2.7", "1.0");
        }
        finally { File.SetAttributes(result.DefinitionsPath, FileAttributes.Normal); }
        File.AppendAllText(result.DefinitionsPath, "\ncorrupted references");
        LuaDefinitionGenerator.Generate(catalog, _workspace, "14.2.7", "1.0");
        Assert.Equal(expected, File.ReadAllText(result.DefinitionsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
    }
}
