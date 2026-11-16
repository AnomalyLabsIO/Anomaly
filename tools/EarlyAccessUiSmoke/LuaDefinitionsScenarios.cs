using System.Text.Json;
using Anomaly.Client.Api.Commands;
using UnityEngine;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private bool _luaDefinitionsSmoke;

    private void EnqueueLuaDefinitionsSmoke()
    {
        Add("lua-definitions-command", () =>
        {
            var workspace = Path.Combine(_output, "author workspace");
            Directory.CreateDirectory(workspace);
            var configuration = Path.Combine(workspace, ".luarc.json");
            const string custom = "{\"diagnostics.globals\":[\"DeveloperOwned\"]}";
            File.WriteAllText(configuration, custom);
            var replies = new List<string>();
            var invocation = "anomaly.luadefs \"" + workspace + "\"";
            Require(CommandRegistry.Execute(invocation, replies.Add), "Installed client dispatches Lua reference command");
            var definitions = Path.Combine(workspace, ".definitions", "anomaly.lua");
            var source = File.ReadAllText(definitions);
            Require(File.ReadAllText(configuration) == custom, "Developer editor configuration is preserved");
            Require(source.Contains("---@class Player", StringComparison.Ordinal) &&
                source.Contains("function _type_GameContext:GetPlayer(playerId)", StringComparison.Ordinal) &&
                source.Contains("function Events.Client.Ready.Add(callback)", StringComparison.Ordinal) &&
                source.Contains("Enums[\"Il2Cpp.ItemType\"]", StringComparison.Ordinal),
                "Installed approved userdata, native queries, events and game enums have editor references");
            Require(!source.Contains("function _type_Player:GetType", StringComparison.Ordinal),
                "Tooling does not expose arbitrary CLR members");
            Require(CommandRegistry.Execute(invocation + " --force", replies.Add), "Explicit configuration replacement dispatches");
            using var settings = JsonDocument.Parse(File.ReadAllText(configuration));
            Require(settings.RootElement.GetProperty("runtime.version").GetString() == "Lua 5.2" &&
                settings.RootElement.GetProperty("workspace.library")[0].GetString() == ".definitions",
                "Generated configuration points LuaLS at the actual definitions using MoonSharp semantics");
            var engine = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "Anomaly.MoonSharp.Interpreter");
            var scriptType = engine.GetType("MoonSharp.Interpreter.Script", true);
            var modulesType = engine.GetType("MoonSharp.Interpreter.CoreModules", true);
            var parser = Activator.CreateInstance(scriptType, Enum.Parse(modulesType, "None"));
            scriptType.GetMethod("LoadString").Invoke(parser, new object[] { source, null, "generated-editor-references" });
            File.WriteAllLines(Path.Combine(_output, "lua-definitions-command.txt"), replies);
            File.WriteAllText(Path.Combine(_output, "lua-definitions-result.json"), JsonSerializer.Serialize(new
            {
                passed = true,
                command = invocation,
                definitions,
                bytes = new FileInfo(definitions).Length,
                customConfigurationPreserved = true,
                forceConfigurationReplaced = true,
                generatedLuaParsed = true,
                gameVersion = new Version(Il2CppGameCore.Version.Major, Il2CppGameCore.Version.Minor,
                    Il2CppGameCore.Version.Revision).ToString(),
                notice = "Actual installed client command and engine parser; editor UI autocomplete not visually certified."
            }, new JsonSerializerOptions { WriteIndented = true }));
        });
        Add("lua-definitions-complete", () =>
        {
            File.WriteAllText(Path.Combine(_output, "result.txt"), "Lua reference command smoke passed.\n");
            Application.Quit();
        });
    }
}
