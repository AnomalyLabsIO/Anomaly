using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Client.Api.Commands;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting;
using Anomaly.Client.Scripting.Catalog;
using MelonLoader;

namespace Anomaly.Client.Commands;

internal sealed class LuaDefinitionsCommand : ICommand
{
    public string Name => "luadefs";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => Tr.Get(TrKeys.Commands_LuaDefinitionsDescription);

    public void Execute(CommandContext context)
    {
        string directory = null;
        bool force = false;
        foreach (var arg in context.Args)
        {
            if (arg == "--force" && !force) force = true;
            else if (directory == null && !arg.StartsWith("--", StringComparison.Ordinal)) directory = arg;
            else
            {
                context.Reply(Tr.Get(TrKeys.Commands_Usage, "anomaly.luadefs [directory] [--force]"));
                return;
            }
        }
        try
        {
            var gameVersion = new Version(Il2CppGameCore.Version.Major, Il2CppGameCore.Version.Minor,
                Il2CppGameCore.Version.Revision).ToString();
            var workspace = Path.GetFullPath(directory ?? Path.Combine("LuaReferences", gameVersion),
                Paths.AnomalyUserDataPath);
            // Metadata only: no VM, connection, source execution or remote worker is created.
            using var runtime = new ScriptRuntime();
            runtime.DiscoverApi();
            var xml = Path.ChangeExtension(typeof(ScriptTypeAttribute).Assembly.Location, ".xml");
            var documentation = File.Exists(xml) ? ScriptXmlDocumentation.Read(xml) : null;
            var result = LuaDefinitionGenerator.Generate(runtime.GetCatalog(), workspace, gameVersion,
                typeof(Core).Assembly.GetName().Version.ToString(), force, documentation);
            context.Reply(Tr.Get(TrKeys.Commands_LuaDefinitionsWritten, result.DefinitionsPath));
            context.Reply(result.ConfigurationPreserved
                ? Tr.Get(TrKeys.Commands_LuaDefinitionsPreserved, result.ConfigurationPath)
                : Tr.Get(TrKeys.Commands_LuaDefinitionsWorkspace, workspace));
        }
        catch (Exception ex)
        {
            MelonLogger.Error("Lua reference generation failed: " + ex);
            context.Reply(Tr.Get(TrKeys.Commands_LuaDefinitionsFailed, ex.GetType().Name));
        }
    }
}
