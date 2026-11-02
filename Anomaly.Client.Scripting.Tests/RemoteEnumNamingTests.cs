using System.Security.Cryptography;
using System.Text;
using Anomaly.Client.Scripting.Remote;
using Anomaly.Client.Scripting.Remote.Protocol;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

[Collection(RemoteWorkerCollection.Name)]
public sealed class RemoteEnumNamingTests(RemoteWorkerFixture fixture)
{
    [Fact]
    public void Exported_worker_names_match_the_clean_runtime_catalog()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        var catalog = runtime.GetCatalog();
        var exported = RemoteHostSession.ExportCatalog(runtime);
        var enums = exported.Required("enums").RequireObject(2048);
        var aliases = exported.Required("enumAliases").RequireObject(1024);
        var globals = exported.Required("enumGlobals").RequireObject(1024);

        Assert.Equal(catalog.Enums.Count, enums.Count);
        foreach (var entry in catalog.Enums)
        {
            Assert.DoesNotContain('+', entry.LuaName);
            Assert.False(entry.LuaName.StartsWith("Il2Cpp", StringComparison.Ordinal));
            Assert.Equal(entry.Members.Count, enums[entry.LuaName].RequireObject(2048).Count);
            if (entry.Alias is { } alias) Assert.Equal(entry.LuaName, aliases[alias].Text);
            if (entry.GlobalName is { } global) Assert.Equal(entry.LuaName, globals[global].Text);
        }
        Assert.True(enums.ContainsKey("PlayerRoles.RoleTypeId"));
        Assert.True(enums.ContainsKey("ActionName"));
        Assert.True(enums.ContainsKey("AlphaWarheadNukesitePanel.DiodeType"));
        Assert.False(enums.ContainsKey("Il2CppPlayerRoles.RoleTypeId"));
        Assert.Equal("AlphaWarheadNukesitePanel.DiodeType", globals["AlphaWarheadNukesitePanel.DiodeType"].Text);
    }

    [WindowsWorkerFact]
    public void Isolated_worker_uses_clean_enum_names_and_readonly_namespace_nodes()
    {
        fixture.RequireQualified();
        var logs = new List<string>();
        using var runtime = new ScriptRuntime(outputLog: logs.Add);
        runtime.DiscoverApi();
        const string source = """
            assert(RoleTypeId.ClassD == 'ClassD')
            assert(ActionName.Jump == 'Jump')
            assert(Enums.PlayerRoles.RoleTypeId.ClassD == RoleTypeId.ClassD)
            assert(Enums['PlayerRoles.RoleTypeId'].ClassD == RoleTypeId.ClassD)
            assert(AlphaWarheadNukesitePanel.DiodeType.BlastDoor == 'BlastDoor')
            assert(Enums.AlphaWarheadNukesitePanel.DiodeType.BlastDoor == 'BlastDoor')
            assert(Il2Cpp == nil and Il2CppPlayerRoles == nil)
            assert(Enums['Il2CppPlayerRoles.RoleTypeId'] == nil)
            assert(Enums['Il2Cpp.AlphaWarheadNukesitePanel+DiodeType'] == nil)
            assert(AlphaWarheadNukesitePanel.GetType == nil)
            assert(AlphaWarheadNukesitePanel.new == nil)
            assert(not pcall(function() AlphaWarheadNukesitePanel.DiodeType = {} end))
            assert(not pcall(function() AlphaWarheadNukesitePanel.DiodeType.BlastDoor = 'changed' end))
            assert(not pcall(function() Enums.PlayerRoles.RoleTypeId = {} end))
            Output:Log('enum names validated')
            """;
        var bytes = Encoding.UTF8.GetBytes(source);
        var script = new ScriptSource("fixture:enum_names", "mods/fixture/enum_names.lua", source,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
        using var scripts = new RemoteConnectionScriptSet(runtime, () => true, activationId: 7);
        bool? completed = null;
        var error = "";
        scripts.BeginInitialize(new[] { script }, true, (success, reason) =>
        {
            completed = success;
            error = reason;
        });
        RemoteWorkerFixture.PumpUntil(() => completed.HasValue, scripts.Tick,
            "Isolated enum initialization did not finish.");
        Assert.True(completed == true, error);
        Assert.True(scripts.IsHealthy, scripts.FailureReason);
        Assert.EndsWith("enum names validated", Assert.Single(logs));
    }
}
