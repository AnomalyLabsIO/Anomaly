using System.Text;
using System.Text.Json;

namespace Anomaly.Client.Scripting.Catalog;

/// <summary>Exports inert editor references from the same catalog that authorizes Lua access.</summary>
internal static class LuaDefinitionGenerator
{
    internal static LuaDefinitionExport Generate(ScriptApiCatalog catalog, string workspaceDirectory,
        string gameVersion, string anomalyVersion, bool force = false,
        IReadOnlyDictionary<string, string>? documentation = null)
    {
        var workspace = Path.GetFullPath(workspaceDirectory);
        var definitions = Path.Combine(workspace, ".definitions", "anomaly.lua");
        var configuration = Path.Combine(workspace, ".luarc.json");
        var text = "-- Anomaly client Lua references. Generated; do not edit.\n" +
            "-- Game: " + gameVersion + "; Anomaly: " + anomalyVersion + "\n" +
            "-- Regenerate in the client console: anomaly.luadefs [directory] [--force]\n" +
            catalog.ToLuaDefinitions(documentation);
        WriteIfChanged(definitions, text, force);

        // An editor configuration belongs to the developer, even if it already references our library.
        // Only an explicit --force replaces it; definitions can still be refreshed independently.
        var preserved = File.Exists(configuration) && !force;
        if (!preserved)
        {
            var settings = new Dictionary<string, object>
            {
                ["runtime.version"] = "Lua 5.2",
                ["runtime.builtin"] = LuaSandboxDefinitions.Builtins,
                ["workspace.library"] = new[] { ".definitions" },
                ["workspace.ignoreDir"] = new[] { ".definitions" },
                ["diagnostics.disable"] = new[] { "lowercase-global" }
            };
            WriteIfChanged(configuration,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }) + "\n", force);
        }
        return new LuaDefinitionExport(definitions, configuration, preserved);
    }

    private static void WriteIfChanged(string path, string text, bool force)
    {
        if (!force && File.Exists(path) && File.ReadAllText(path) == text) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}

internal readonly record struct LuaDefinitionExport(string DefinitionsPath, string ConfigurationPath,
    bool ConfigurationPreserved);
