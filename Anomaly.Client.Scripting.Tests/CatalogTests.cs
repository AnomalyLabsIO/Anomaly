using System.Text.Json;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Catalog;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void Runtime_policy_and_exports_share_the_same_immutable_member_metadata()
    {
        var registry = CatalogRegistry();
        var catalog = registry.GetCatalog();
        var target = catalog.Types.Single(type => type.Name == nameof(CatalogTarget));
        var policy = registry.GetTypePolicy(typeof(CatalogTarget));
        Assert.Same(target, policy.Catalog);
        Assert.Same(target.Members.Single(member => member.Name == nameof(CatalogTarget.Name)), policy.Members[nameof(CatalogTarget.Name)].Catalog);
        Assert.False(target.Members.Single(member => member.Name == nameof(CatalogTarget.Name)).AllowWrite);
        Assert.True(target.Members.Single(member => member.Name == nameof(CatalogTarget.Counter)).AllowWrite);
        Assert.Throws<NotSupportedException>(() => ((IList<ScriptTypeCatalog>)catalog.Types).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ScriptMemberCatalog>)target.Members).Clear());
    }

    [Fact]
    public void Export_order_is_deterministic_and_takes_a_snapshot_of_registered_types()
    {
        var first = CatalogRegistry();
        var second = new ScriptBindingRegistry();
        second.RegisterType<CatalogChild>();
        second.RegisterType<CatalogTarget>();
        var initial = first.GetCatalog();
        Assert.Equal(initial.DescribeSurface(), first.DescribeSurface());
        Assert.Equal(initial.ToJson(), second.GetCatalog().ToJson());
        Assert.DoesNotContain('\r', initial.ToJson());
        Assert.Equal(initial.ToLuaDefinitions(), second.GetCatalog().ToLuaDefinitions());
        first.RegisterType<AnotherCatalogTarget>();
        Assert.DoesNotContain(initial.Types, type => type.Name == nameof(AnotherCatalogTarget));
        Assert.Contains(first.GetCatalog().Types, type => type.Name == nameof(AnotherCatalogTarget));
    }

    [Fact]
    public void Export_does_not_construct_instances_or_execute_getters()
    {
        CatalogTarget.GetterCalls = 0;
        var catalog = CatalogRegistry().GetCatalog();
        _ = catalog.DescribeSurface();
        _ = catalog.ToJson();
        _ = catalog.ToLuaDefinitions();
        Assert.Equal(0, CatalogTarget.GetterCalls);
    }

    [Fact]
    public void Json_manifest_has_only_approved_members_and_preserves_documentation_identity()
    {
        var catalog = CatalogRegistry().GetCatalog();
        var member = catalog.Types.Single(type => type.Name == nameof(CatalogTarget)).Members.Single(value => value.Name == nameof(CatalogTarget.Name));
        var documentation = new Dictionary<string, string> { [member.DocumentationId] = "Approved name." };
        using var json = JsonDocument.Parse(catalog.ToJson(documentation));
        var type = json.RootElement.GetProperty("types").EnumerateArray().Single(value => value.GetProperty("name").GetString() == nameof(CatalogTarget));
        var entries = type.GetProperty("members").EnumerateArray().ToArray();
        Assert.DoesNotContain(entries, value => value.GetProperty("name").GetString() is "Secret" or "GetType" or "ToString");
        var name = entries.Single(value => value.GetProperty("name").GetString() == nameof(CatalogTarget.Name));
        Assert.Equal("Approved name.", name.GetProperty("documentation").GetString());
        Assert.Equal("Property", name.GetProperty("kind").GetString());
        Assert.False(name.GetProperty("allowWrite").GetBoolean());
    }

    [Fact]
    public void Lua_definitions_preserve_case_and_are_valid_source_without_fake_type_globals()
    {
        var catalog = CatalogRegistry().GetCatalog();
        var lua = catalog.ToLuaDefinitions();
        Assert.Contains("---@class CatalogTarget", lua);
        Assert.Contains("---@field Name string|nil # Read-only.", lua);
        Assert.Contains("function _type_CatalogTarget:Echo(input)", lua);
        Assert.Contains("function _type_CatalogTarget:KeywordParameter(arg0)", lua);
        Assert.Contains("function _type_CatalogTarget:KeywordCollision(arg0_1, arg0)", lua);
        Assert.DoesNotContain("\nCatalogTarget =", lua);
        Assert.DoesNotContain("GetType", lua);
        new Script(CoreModules.None).LoadString(lua);
    }

    [Fact]
    public void Event_metadata_describes_the_global_subscription_shape_and_token_contract()
    {
        var eventParameters = new[] { new ScriptParameterCatalog("args", typeof(CatalogChild).FullName!) };
        var entry = new ScriptEventCatalogEntry("Client", "Ready", "Example.ClientEvents", "Ready", eventParameters);
        var catalog = CatalogRegistry().GetCatalog().WithEvents(new[] { entry });
        eventParameters[0] = new ScriptParameterCatalog("changed", "System.Int32");
        Assert.Equal("args", entry.Parameters[0].Name);
        Assert.True(catalog.HasEventsGlobal);
        Assert.Contains("event Events.Client.Ready(args:", catalog.DescribeSurface());
        using var json = JsonDocument.Parse(catalog.ToJson());
        var evt = json.RootElement.GetProperty("events")[0];
        Assert.Equal("System.String", evt.GetProperty("add").GetProperty("returnType").GetString());
        Assert.Equal("System.Boolean", evt.GetProperty("remove").GetProperty("returnType").GetString());
        Assert.Equal("function|string", evt.GetProperty("remove").GetProperty("parameters")[0].GetProperty("clrType").GetString());
        var lua = catalog.ToLuaDefinitions();
        Assert.Contains("function Events.Client.Ready.Add(callback)", lua);
        Assert.Contains("function Events.Client.Ready.Remove(callbackOrToken)", lua);
        new Script(CoreModules.None).LoadString(lua);
    }

    [Fact]
    public void Xml_summaries_are_optional_and_only_selected_ids_reach_exports()
    {
        var catalog = CatalogRegistry().GetCatalog();
        var member = catalog.Types.Single(type => type.Name == nameof(CatalogTarget)).Members.Single(value => value.Name == nameof(CatalogTarget.Name));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"<doc><members><member name='{member.DocumentationId}'><summary> A documented\n name. </summary></member><member name='P:Hidden.Secret'><summary>HIDDEN DOCUMENTATION</summary></member></members></doc>");
            var documentation = ScriptXmlDocumentation.Read(path);
            Assert.Equal("A documented name.", documentation[member.DocumentationId]);
            Assert.Contains("A documented name.", catalog.ToLuaDefinitions(documentation));
            Assert.DoesNotContain("HIDDEN DOCUMENTATION", catalog.ToJson(documentation));
        }
        finally { File.Delete(path); }
    }

    private static ScriptBindingRegistry CatalogRegistry()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<CatalogTarget>();
        return registry;
    }

    [ScriptType]
    public sealed class CatalogTarget
    {
        public static int GetterCalls;
        [ScriptVisible] public string Name { get { GetterCalls++; return "example"; } }
        [ScriptVisible(AllowWrite = true)] public int Counter { get; set; }
        [ScriptVisible] public CatalogChild Child => new();
        [ScriptVisible] public string Echo(string input) => input;
        [ScriptVisible] public int KeywordParameter(int @end) => @end;
        [ScriptVisible] public int KeywordCollision(int @end, int arg0) => @end + arg0;
        [ScriptVisible, ScriptIgnore] public object Secret => new();
    }

    [ScriptType]
    public sealed class CatalogChild { [ScriptVisible] public bool Enabled => true; }
    [ScriptType]
    public sealed class AnotherCatalogTarget { [ScriptVisible] public int Value => 1; }
}
