using System.Reflection;
using System.Reflection.Emit;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Remote.Protocol;
using Anomaly.Shared.Networking.Messages;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class EnumNamingTests
{
    [Theory]
    [InlineData("Il2CppPlayerRoles.RoleTypeId", "PlayerRoles.RoleTypeId")]
    [InlineData("Il2Cpp.ActionName", "ActionName")]
    [InlineData("Il2Cpp.AlphaWarheadNukesitePanel+DiodeType", "AlphaWarheadNukesitePanel.DiodeType")]
    [InlineData("Il2Cpp.Il2CppPlayerRoles.RoleTypeId", "PlayerRoles.RoleTypeId")]
    [InlineData("Example.Il2CppOwner+Kind", "Example.Il2CppOwner.Kind")]
    public void Canonical_paths_strip_only_the_leading_interop_prefix(string clrName, string expected)
    {
        Assert.Equal(expected, EnumNameRules.Clean(clrName));
        Assert.Equal(expected, EnumNameRules.Clean(expected));
    }

    [Fact]
    public void Normalized_identity_collisions_reject_the_whole_discovery_batch()
    {
        var assembly = BuildAssembly("Il2CppExample.Kind", "Example.Kind");
        var registry = new ScriptEnumRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.Discover(assembly));
        Assert.Empty(registry.GetCatalog());
        Assert.All(assembly.GetTypes().Where(type => type.IsEnum), type => Assert.False(registry.Contains(type)));

        // A failed preflight has not selected or partially committed the trusted assembly.
        registry.Discover(BuildAssembly("Healthy.Kind"));
        Assert.Equal("Healthy.Kind", Assert.Single(registry.GetCatalog()).LuaName);
    }

    [Fact]
    public void Canonical_enum_leaves_cannot_also_be_namespace_nodes()
    {
        var registry = new ScriptEnumRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.Discover(
            BuildAssembly("Il2CppExample.Kind", "Example.Kind.Child")));
        Assert.Empty(registry.GetCatalog());
    }

    [Fact]
    public void Duplicate_relative_names_use_qualified_fallbacks_without_a_first_winner()
    {
        var registry = new ScriptEnumRegistry();
        registry.Discover(BuildAssembly("Il2CppFirst.Mode", "Il2CppSecond.Mode"));
        Assert.All(registry.GetCatalog(), entry =>
        {
            Assert.Null(entry.Alias);
            Assert.Equal(entry.LuaName, entry.GlobalName);
        });

        var script = NewScript(registry);
        Assert.True(script.DoString("return Mode == nil and Enums.Mode == nil and " +
            "First.Mode.Value == 'Value' and Second.Mode.Value == 'Value'").Boolean);
    }

    [Fact]
    public void Relative_names_that_conflict_with_other_qualified_names_are_not_installed()
    {
        var registry = new ScriptEnumRegistry();
        registry.Discover(BuildAssembly("First.Kind", "Kind.Other"));
        var first = registry.GetCatalog().Single(entry => entry.ClrType == "First.Kind");
        Assert.Null(first.Alias);
        Assert.Equal("First.Kind", first.GlobalName);

        var script = NewScript(registry);
        Assert.True(script.DoString("return First.Kind.Value == 'Value' and Other.Value == 'Value' and " +
            "Enums.Kind.Other.Value == 'Value' and Enums.Kind.Value == nil").Boolean);
    }

    [Fact]
    public void An_enum_cannot_use_a_relative_alias_that_contains_its_own_namespace_branch()
    {
        var registry = new ScriptEnumRegistry();
        registry.Discover(BuildAssembly("Owner.Owner"));
        var entry = Assert.Single(registry.GetCatalog());
        Assert.Null(entry.Alias);
        Assert.Equal("Owner.Owner", entry.GlobalName);
        Assert.Equal("Value", NewScript(registry).DoString("return Owner.Owner.Value").String);
    }

    [Fact]
    public void Reserved_global_roots_use_safe_qualified_fallbacks_or_remain_registry_only()
    {
        var registry = new ScriptEnumRegistry();
        registry.Discover(BuildAssembly("Example.Game", "Game.Game"));
        var script = new Script(CoreModules.None);
        script.Globals.Set("Game", DynValue.NewString("host capability"));
        registry.InstallGlobals(script);

        Assert.Equal("Example.Game", registry.GetCatalog().Single(entry => entry.ClrType == "Example.Game").GlobalName);
        Assert.Null(registry.GetCatalog().Single(entry => entry.ClrType == "Game.Game").GlobalName);
        Assert.True(script.DoString("return Game == 'host capability' and Example.Game.Value == 'Value' and " +
            "Enums['Game.Game'].Value == 'Value'").Boolean);
    }

    [Fact]
    public void Invalid_identifier_paths_keep_bounded_clean_bracket_lookup()
    {
        var registry = new ScriptEnumRegistry();
        registry.Discover(BuildAssembly("Il2CppExample.end"));
        var entry = Assert.Single(registry.GetCatalog());
        Assert.Equal("Example.end", entry.LuaName);
        Assert.Null(entry.Alias);
        Assert.Null(entry.GlobalName);
        var script = NewScript(registry);
        Assert.Equal("Value", script.DoString("return Enums['Example.end'].Value").String);
        Assert.True(script.Globals.Get("Example").IsNil());
        Assert.True(script.DoString("return Enums['Il2CppExample.end'] == nil").Boolean);
    }

    [Fact]
    public void Existing_global_root_collisions_are_checked_before_any_installation()
    {
        var registry = new ScriptEnumRegistry();
        registry.Discover(BuildAssembly("Il2CppSimple", "Il2Cpp.Owner+Kind"));
        var script = new Script(CoreModules.None);
        script.Globals.Set("Owner", DynValue.NewString("existing"));

        Assert.Throws<InvalidOperationException>(() => registry.InstallGlobals(script));
        Assert.True(script.Globals.Get("Enums").IsNil());
        Assert.True(script.Globals.Get("Simple").IsNil());
        Assert.Equal("existing", script.Globals.Get("Owner").String);
    }

    [Fact]
    public void Nested_owner_nodes_are_read_only_session_owned_and_protected_host_roots()
    {
        var bindings = new ScriptBindingRegistry();
        bindings.Enums.Discover(BuildAssembly("Il2Cpp.Owner+Kind", "Il2Cpp.Owner+Other"));
        var first = NewScript(bindings.Enums);
        Assert.True(first.DoString("return Owner.Kind.Value == 'Value' and Owner.Other.Value == 'Value' and " +
            "Enums.Owner.Kind.Value == 'Value' and Enums['Owner.Kind'].Value == 'Value'").Boolean);
        Assert.True(first.DoString("return Owner.GetType == nil and Owner.new == nil and Owner.Kind.Parse == nil").Boolean);
        Assert.Throws<ScriptRuntimeException>(() => first.DoString("Owner.Kind = {}"));
        Assert.Throws<ScriptRuntimeException>(() => first.DoString("Enums.Owner.New = {}"));
        Assert.Throws<ScriptRuntimeException>(() => first.DoString("Owner.Kind.Value = 'Other'"));

        var second = NewScript(bindings.Enums);
        second.Globals.Set("foreign", first.Globals.Get("Owner"));
        Assert.Throws<ScriptRuntimeException>(() => second.DoString("return foreign.Kind.Value"));
        second.Globals.Set("foreign", first.DoString("return Owner.Kind"));
        Assert.Throws<ScriptRuntimeException>(() => second.DoString("return foreign.Value"));

        Assert.True(bindings.Enums.IsGlobalName("Owner"));
        using var session = new ScriptSession(bindings, new ScriptExecutionOptions(), null);
        Assert.Throws<ArgumentException>(() => session.SetGlobal("Owner", null));
    }

    [Fact]
    public void Alias_planning_does_not_depend_on_reflection_or_input_order()
    {
        var paths = new[] { "First.Mode", "Second.Mode", "Il2Cpp.Owner+Kind", "Il2Cpp.ActionName", "Owner.Owner" };
        var first = new ScriptEnumRegistry();
        var second = new ScriptEnumRegistry();
        first.Discover(BuildAssembly(paths));
        second.Discover(BuildAssembly(paths.AsEnumerable().Reverse().ToArray()));
        Assert.Equal(first.GetCatalog().Select(entry => (entry.ClrType, entry.LuaName, entry.Alias, entry.GlobalName)),
            second.GetCatalog().Select(entry => (entry.ClrType, entry.LuaName, entry.Alias, entry.GlobalName)));
    }

    [Fact]
    public void Every_real_game_enum_has_a_clean_callable_path_with_matching_constants()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        var entries = runtime.GetCatalog().Enums;
        var expected = typeof(Il2Cpp.ItemType).Assembly.GetTypes()
            .Where(type => type.IsVisible && type.IsEnum && !type.ContainsGenericParameters)
            .Concat(new[] { typeof(RoundPhase) }).Distinct()
            .Select(type => type.FullName!).OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(expected, entries.Select(entry => entry.ClrType));

        var script = NewScript(runtime.Bindings.Enums);
        foreach (var entry in entries)
        {
            Assert.NotNull(entry.GlobalName);
            Assert.False(entry.LuaName.StartsWith("Il2Cpp", StringComparison.Ordinal));
            Assert.False(entry.GlobalName!.StartsWith("Il2Cpp", StringComparison.Ordinal));
            var member = entry.Members.FirstOrDefault();
            if (member != null)
            {
                Assert.Equal(member.Value, script.DoString("return " + entry.GlobalName + "[" + Quote(member.Name) + "]").String);
                Assert.Equal(member.Value, script.DoString("return Enums[" + Quote(entry.LuaName) + "][" + Quote(member.Name) + "]").String);
            }
            if (entry.ClrType.StartsWith("Il2Cpp", StringComparison.Ordinal))
                Assert.True(script.DoString("return Enums[" + Quote(entry.ClrType) + "] == nil").Boolean);
        }
        Assert.Equal("ClassD", script.DoString("return RoleTypeId.ClassD").String);
        Assert.Equal("Jump", script.DoString("return ActionName.Jump").String);
        Assert.Equal("BlastDoor", script.DoString("return AlphaWarheadNukesitePanel.DiodeType.BlastDoor").String);
    }

    private static Script NewScript(ScriptEnumRegistry registry)
    {
        var script = new Script(CoreModules.None);
        registry.InstallGlobals(script);
        return script;
    }

    private static string Quote(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    // Dynamic fixtures stay out of this assembly's broad enum discovery and baseline counts.
    private static Assembly BuildAssembly(params string[] enumPaths)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("EnumNaming" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Enums");
        var owners = new Dictionary<string, TypeBuilder>(StringComparer.Ordinal);
        TypeBuilder Owner(string path)
        {
            if (owners.TryGetValue(path, out var existing)) return existing;
            var split = path.LastIndexOf('+');
            var owner = split < 0
                ? module.DefineType(path, TypeAttributes.Public | TypeAttributes.Class)
                : Owner(path[..split]).DefineNestedType(path[(split + 1)..], TypeAttributes.NestedPublic | TypeAttributes.Class);
            owners.Add(path, owner);
            return owner;
        }
        foreach (var path in enumPaths)
        {
            var split = path.LastIndexOf('+');
            if (split < 0)
            {
                var type = module.DefineEnum(path, TypeAttributes.Public, typeof(int));
                type.DefineLiteral("Value", 0);
                type.CreateTypeInfo();
                continue;
            }
            var nested = Owner(path[..split]).DefineNestedType(path[(split + 1)..],
                TypeAttributes.NestedPublic | TypeAttributes.Sealed, typeof(Enum));
            nested.DefineField("value__", typeof(int), FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName);
            nested.DefineField("Value", nested, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal).SetConstant(0);
            nested.CreateTypeInfo();
        }
        foreach (var owner in owners.OrderByDescending(entry => entry.Key.Count(value => value == '+')))
            owner.Value.CreateTypeInfo();
        // Discovery checks the exact runtime assembly identity, not the emit-builder wrapper.
        return assembly.GetTypes().First(type => type.IsEnum).Assembly;
    }
}
