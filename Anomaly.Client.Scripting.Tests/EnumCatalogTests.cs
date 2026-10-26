using System.Text.Json;
using Anomaly.Client.Scripting.Catalog;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class EnumCatalogTests
{
    [Fact]
    public void Enum_metadata_is_immutable_and_survives_event_and_message_catalog_composition()
    {
        var members = new[] { new ScriptEnumMemberCatalog("High", "High", "18446744073709551615") };
        var entry = new ScriptEnumCatalog("Game.Owner+Flags", "Flags", null, true, members);
        var enums = new[] { entry };
        var catalog = new ScriptApiCatalog(Array.Empty<ScriptTypeCatalog>(), enums: enums);
        members[0] = new ScriptEnumMemberCatalog("Changed", "Changed", "0");
        enums[0] = new ScriptEnumCatalog("Changed", null, null, false, Array.Empty<ScriptEnumMemberCatalog>());
        var composed = catalog.WithEvents(Array.Empty<ScriptEventCatalogEntry>()).WithMessages();
        Assert.Same(entry, Assert.Single(composed.Enums));
        Assert.Equal("High", Assert.Single(entry.Members).Name);
        Assert.Throws<NotSupportedException>(() => ((IList<ScriptEnumCatalog>)catalog.Enums).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ScriptEnumMemberCatalog>)entry.Members).Clear());
        using var json = JsonDocument.Parse(composed.ToJson());
        var metadata = Assert.Single(json.RootElement.GetProperty("enums").EnumerateArray());
        Assert.Equal("string", metadata.GetProperty("representation").GetString());
        Assert.True(metadata.GetProperty("caseInsensitiveInput").GetBoolean());
        Assert.Equal("18446744073709551615", metadata.GetProperty("members")[0].GetProperty("numericValue").GetString());
        Assert.Contains("global Enums: readonly-enum-registry", composed.DescribeSurface());
    }

    [Fact]
    public void Lua_definitions_preserve_canonical_paths_aliases_and_exact_string_values()
    {
        var entry = new ScriptEnumCatalog("Game.Owner+Flags", "Flags", "GameFlags", true, new[]
        {
            new ScriptEnumMemberCatalog("end", "Quoted\"\\\n123", "1"),
            new ScriptEnumMemberCatalog("Alias", "Quoted\"\\\n123", "1")
        });
        var catalog = new ScriptApiCatalog(Array.Empty<ScriptTypeCatalog>(), enums: new[] { entry });
        var lua = catalog.ToLuaDefinitions();
        Assert.Contains("Enums.Flags = Enums[\"Game.Owner+Flags\"]", lua);
        Assert.Contains("GameFlags = Enums[\"Game.Owner+Flags\"]", lua);
        Assert.Contains("[\"end\"]", lua);
        var script = new Script(CoreModules.None);
        script.DoString(lua);
        Assert.Equal("Quoted\"\\\n123", script.DoString("return GameFlags['end']").String);
        Assert.True(script.DoString("return GameFlags == Enums.Flags and GameFlags.Alias == GameFlags['end']").Boolean);
        Assert.Contains("  alias Enums.Flags", catalog.DescribeSurface());
    }

    [Fact]
    public void Enum_exports_are_deterministic_and_do_not_use_one_lua_local_per_enum()
    {
        var enums = Enumerable.Range(0, 300).Select(index => new ScriptEnumCatalog("Game.Enum" + index,
            "Enum" + index, null, false, new[] { new ScriptEnumMemberCatalog("Value", "Value", "0") })).ToArray();
        var first = new ScriptApiCatalog(Array.Empty<ScriptTypeCatalog>(), enums: enums);
        var second = new ScriptApiCatalog(Array.Empty<ScriptTypeCatalog>(), enums: enums.AsEnumerable().Reverse());
        Assert.Equal(first.DescribeSurface(), second.DescribeSurface());
        Assert.Equal(first.ToJson(), second.ToJson());
        Assert.Equal(first.ToLuaDefinitions(), second.ToLuaDefinitions());
        new Script(CoreModules.None).LoadString(first.ToLuaDefinitions());
    }

    [Fact]
    public void Enum_member_signatures_use_string_values_in_lua_definitions()
    {
        var entry = new ScriptEnumCatalog("Game.Kind", "Kind", "Kind", false,
            new[] { new ScriptEnumMemberCatalog("None", "None", "0") });
        var type = new ScriptTypeCatalog("Target", "Target", new[]
        {
            new ScriptMemberCatalog(ScriptMemberKind.Property, "Kind", "Kind", "Game.Kind", true,
                Array.Empty<ScriptParameterCatalog>(), "P:Target.Kind"),
            new ScriptMemberCatalog(ScriptMemberKind.Method, "Echo", "Echo", "Game.Kind", false,
                new[] { new ScriptParameterCatalog("kind", "Game.Kind") }, "M:Target.Echo(Game.Kind)")
        });
        var lua = new ScriptApiCatalog(new[] { type }, enums: new[] { entry }).ToLuaDefinitions();
        Assert.Contains("---@field Kind string # Writable.", lua);
        Assert.Contains("---@param kind string", lua);
        Assert.Contains("---@return string", lua);
    }

    [Fact]
    public void Ambiguous_enum_lookup_names_and_globals_are_rejected()
    {
        var members = Array.Empty<ScriptEnumMemberCatalog>();
        var first = new ScriptEnumCatalog("Game.First", "Kind", "First", false, members);
        var second = new ScriptEnumCatalog("Game.Second", "Kind", "Second", false, members);
        Assert.Throws<InvalidOperationException>(() => new ScriptApiCatalog(Array.Empty<ScriptTypeCatalog>(), enums: new[] { first, second }));
        var reserved = new ScriptEnumCatalog("Game.Reserved", null, "Enums", false, members);
        Assert.Throws<InvalidOperationException>(() => new ScriptApiCatalog(Array.Empty<ScriptTypeCatalog>(), enums: new[] { reserved }));
        var canonicalCollision = new ScriptEnumCatalog("Kind", null, null, false, members);
        Assert.Throws<InvalidOperationException>(() => new ScriptApiCatalog(Array.Empty<ScriptTypeCatalog>(), enums: new[] { first, canonicalCollision }));
    }
}
