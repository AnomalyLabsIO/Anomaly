using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Events;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class EnumBindingTests
{
    [Fact]
    public void Declared_constants_and_case_insensitive_strings_round_trip_through_methods_and_setters()
    {
        var (_, script, target) = BoundConsumer();
        script.DoString("target.Color = EnumTestColor.Green; target:SetColor('bLuE')");
        Assert.Equal(EnumTestColor.Blue, target.Color);
        Assert.Equal("Green", script.DoString("return target:EchoColor(' gReEn ')").String);
        Assert.True(script.DoString("return target.Color == EnumTestColor.Blue and " +
            "Enums.EnumTestColor.Red == Enums.EnumTestColor.Scarlet").Boolean);
        Assert.Equal(EnumTestColor.Red.ToString(), script.DoString("return EnumTestColor.Scarlet").String);
    }

    [Theory]
    [InlineData("target.Color = 'unknown'")]
    [InlineData("target.Color = '1'")]
    [InlineData("target.Color = 1")]
    [InlineData("target.Color = true")]
    [InlineData("target.Color = nil")]
    [InlineData("target.Color = {}")]
    [InlineData("target.Color = ''")]
    [InlineData("target.Color = '   '")]
    [InlineData("target.Color = 'Red, Green'")]
    [InlineData("target.Color = string.rep('x', 4097)")]
    public void Invalid_enum_inputs_do_not_reach_the_host_setter(string source)
    {
        var (_, script, target) = BoundConsumer();
        Assert.Throws<ScriptRuntimeException>(() => script.DoString(source));
        Assert.Equal(EnumTestColor.Red, target.Color);
        Assert.Equal(0, target.SetterCalls);
    }

    [Fact]
    public void Flags_accept_declared_name_combinations_without_losing_unsigned_64_bit_values()
    {
        var (_, script, target) = BoundConsumer();
        script.DoString("target.Flags = 'hIgH, LOW'");
        Assert.Equal(EnumTestFlags.High | EnumTestFlags.Low, target.Flags);
        Assert.Equal((EnumTestFlags.High | EnumTestFlags.Low).ToString(),
            script.DoString("return target:EchoFlags(' Low , High ')").String);
        Assert.Equal("High", script.DoString("return target:EchoFlags(EnumTestFlags.High)").String);
        Assert.Equal("None", script.DoString("return target:EchoFlags('none')").String);
    }

    [Fact]
    public void Unnamed_flags_zero_round_trips_without_admitting_other_numeric_strings()
    {
        var (_, script, _) = BoundConsumer();
        Assert.Equal("0", script.DoString("return target:EchoUnnamedFlags('0')").String);
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return target:EchoUnnamedFlags('1')"));
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return target:EchoColor('0')"));
    }

    [Fact]
    public void Signed_flags_round_trip_sign_bit_combinations_and_negative_all_mask()
    {
        var (registry, script, _) = BoundConsumer();
        foreach (var value in new[] { EnumSignedFlags.Sign, EnumSignedFlags.Sign | EnumSignedFlags.Low, EnumSignedFlags.All })
        {
            var formatted = registry.ToScriptValue(script, value);
            Assert.Equal(DataType.String, formatted.Type);
            Assert.Equal(value, Assert.IsType<EnumSignedFlags>(registry.FromScriptValue(script, formatted, typeof(EnumSignedFlags))));
        }
        Assert.Equal(EnumSignedFlags.All, registry.Enums.Parse(typeof(EnumSignedFlags), "aLl"));
        Assert.Throws<ScriptRuntimeException>(() => registry.Enums.Parse(typeof(EnumSignedFlags), "-1"));
    }

    [Fact]
    public void Overlapping_composite_flags_round_trip_and_name_lists_have_a_bounded_count()
    {
        var (registry, script, _) = BoundConsumer();
        var combined = EnumCompositeFlags.A | EnumCompositeFlags.B;
        var formatted = registry.ToScriptValue(script, combined);
        Assert.Equal(combined,
            Assert.IsType<EnumCompositeFlags>(registry.FromScriptValue(script, formatted, typeof(EnumCompositeFlags))));
        Assert.Equal(EnumCompositeFlags.A,
            registry.Enums.Parse(typeof(EnumCompositeFlags), string.Join(",", Enumerable.Repeat("A", 64))));
        Assert.Throws<ScriptRuntimeException>(() => registry.Enums.Parse(typeof(EnumCompositeFlags),
            string.Join(",", Enumerable.Repeat("A", 65))));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("9223372036854775808")]
    [InlineData("Low, 2")]
    [InlineData("Low, Unknown")]
    [InlineData("Low,,High")]
    [InlineData("Low,")]
    [InlineData(",High")]
    public void Flags_reject_numeric_or_missing_names(string input)
    {
        var (_, script, target) = BoundConsumer();
        script.Globals.Set("input", DynValue.NewString(input));
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("target.Flags = input"));
        Assert.Equal(EnumTestFlags.None, target.Flags);
    }

    [Fact]
    public void Nested_names_keep_declaring_paths_without_exposing_owner_members()
    {
        var (registry, script, _) = BoundConsumer();
        var first = typeof(EnumOwnerA.Mode).FullName!.Replace('+', '.');
        var second = typeof(EnumOwnerB.Mode).FullName!.Replace('+', '.');
        Assert.Equal("First", script.DoString($"return Enums['{first}'].First").String);
        Assert.Equal("Second", script.DoString($"return Enums['{second}'].Second").String);
        Assert.True(script.DoString("return Enums.Mode == nil and Mode == nil and EnumOwnerA.Mode.First == 'First' and " +
            "EnumOwnerB.Mode.Second == 'Second' and Enums.EnumOwnerA.Mode.First == 'First' and " +
            "EnumOwnerA.Sensitive == nil and EnumOwnerA.new == nil").Boolean);
        Assert.False(registry.Enums.Contains(typeof(PrivateEnum)));
        Assert.False(registry.Enums.Contains(typeof(GenericEnumOwner<>.Mode)));
        Assert.False(registry.Enums.Contains(typeof(EnumOwnerA)));
    }

    [Theory]
    [InlineData("Enums.EnumTestColor = {}")]
    [InlineData("EnumTestColor.Red = 'Green'")]
    [InlineData("EnumTestColor.New = 'New'")]
    public void Enum_nodes_are_read_only_and_do_not_expose_clr_members(string source)
    {
        var (_, script, _) = BoundConsumer();
        Assert.Throws<ScriptRuntimeException>(() => script.DoString(source));
        Assert.True(script.DoString("return EnumTestColor.GetType == nil and EnumTestColor.Parse == nil and " +
            "EnumTestColor.new == nil and EnumTestColor.New == nil").Boolean);
    }

    [Fact]
    public void Enum_proxies_copied_by_the_host_cannot_be_used_in_another_script()
    {
        var (registry, first, _) = BoundConsumer();
        var second = NewScript();
        registry.Enums.InstallGlobals(second);
        second.Globals.Set("foreign", first.Globals.Get("EnumTestColor"));
        Assert.Throws<ScriptRuntimeException>(() => second.DoString("return foreign.Red"));
        second.Globals.Set("foreign", first.Globals.Get("Enums"));
        Assert.Throws<ScriptRuntimeException>(() => second.DoString("return foreign.EnumTestColor"));
        Assert.Equal("Green", second.DoString("return EnumTestColor.Green").String);
    }

    [Fact]
    public void Enum_signatures_and_host_values_require_explicit_enum_assembly_registration()
    {
        var registry = new ScriptBindingRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.RegisterType<EnumConsumer>());
        Assert.Empty(registry.GetCatalog().Types);
        var script = NewScript();
        Assert.Throws<ScriptRuntimeException>(() => registry.ToScriptValue(script, EnumTestColor.Red));
        registry.Enums.Discover(typeof(EnumTestColor).Assembly);
        registry.RegisterType<EnumConsumer>();
        Assert.Equal("Green", registry.ToScriptValue(script, EnumTestColor.Green).String);
    }

    [Fact]
    public void Undefined_host_enum_values_are_rejected_instead_of_becoming_numeric_strings()
    {
        var (registry, script, _) = BoundConsumer();
        Assert.Throws<ScriptRuntimeException>(() => registry.ToScriptValue(script, (EnumTestColor)999));
        Assert.Throws<ScriptRuntimeException>(() => registry.ToScriptValue(script, (EnumTestFlags)2UL));
    }

    [Fact]
    public void Enum_event_snapshots_capture_named_values_and_preserve_exact_type_checks()
    {
        var (registry, script, _) = BoundConsumer();
        var plan = ScriptEventSnapshotPlan.Create(typeof(EnumTestFlags), registry);
        var captured = plan.Capture(EnumTestFlags.High | EnumTestFlags.Low);
        Assert.Equal((EnumTestFlags.High | EnumTestFlags.Low).ToString(), Assert.IsType<string>(captured));
        Assert.Equal(captured, ScriptFrozenEventValue.ToScriptValue(script, captured).String);
        Assert.Throws<InvalidOperationException>(() => plan.Capture(EnumTestColor.Red));
    }

    [Fact]
    public void Nested_readonly_enum_event_properties_are_copied_before_host_mutation()
    {
        var (registry, script, _) = BoundConsumer();
        registry.RegisterType<EnumSnapshotRoot>();
        var source = new EnumSnapshotRoot();
        var plan = ScriptEventSnapshotPlan.Create(typeof(EnumSnapshotRoot), registry);
        var captured = plan.Capture(source);
        source.Child.ColorValue = EnumTestColor.Blue;
        source.Child.FlagsValue = EnumTestFlags.Low;
        script.Globals.Set("snapshot", ScriptFrozenEventValue.ToScriptValue(script, captured));
        Assert.True(script.DoString("return snapshot.Child.Color == EnumTestColor.Green and " +
            "snapshot.Child.Flags == EnumTestFlags.High").Boolean);
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("snapshot.Child.Color = EnumTestColor.Red"));
    }

    [Fact]
    public void Production_discovery_installs_base_game_enums_before_scripts_run()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        Assert.True(runtime.Bindings.Enums.Contains(typeof(Il2Cpp.ItemType)));
        Assert.True(runtime.Bindings.Enums.Contains(typeof(Il2CppPlayerRoles.RoleTypeId)));
        using var session = runtime.CreateDevelopmentSession();
        var result = session.Load("assert(ItemType.KeycardJanitor == 'KeycardJanitor'); " +
            "assert(RoleTypeId.ClassD == Enums['PlayerRoles.RoleTypeId'].ClassD); " +
            "assert(Enums.RoleTypeId.ClassD == 'ClassD'); " +
            "assert(ActionName.Jump == 'Jump'); " +
            "assert(AlphaWarheadNukesitePanel.DiodeType.BlastDoor == 'BlastDoor'); " +
            "assert(Enums['Il2CppPlayerRoles.RoleTypeId'] == nil)");
        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public void Production_catalog_matches_every_visible_game_enum_and_declared_constant()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        var assembly = typeof(Il2Cpp.ItemType).Assembly;
        var catalog = runtime.GetCatalog();
        var roundPhase = typeof(Anomaly.Shared.Networking.Messages.RoundPhase);
        Assert.Equal(assembly.GetTypes().Count(type => type.IsVisible && type.IsEnum && !type.ContainsGenericParameters) + 1,
            catalog.Enums.Count);
        foreach (var entry in catalog.Enums)
        {
            var type = entry.ClrType == roundPhase.FullName ? roundPhase : assembly.GetType(entry.ClrType, throwOnError: true)!;
            foreach (var member in entry.Members)
            {
                var parsed = runtime.Bindings.Enums.Parse(type, member.Name);
                Assert.Equal(Enum.Parse(type, member.Name), parsed);
                Assert.Equal(member.Value, runtime.Bindings.Enums.Format(type, parsed));
            }
        }
    }

    private static (ScriptBindingRegistry Registry, Script Script, EnumConsumer Target) BoundConsumer()
    {
        var registry = new ScriptBindingRegistry();
        registry.Enums.Discover(typeof(EnumTestColor).Assembly);
        registry.RegisterType<EnumConsumer>();
        var script = NewScript();
        registry.Enums.InstallGlobals(script);
        var target = new EnumConsumer();
        registry.SetGlobal(script, "target", target);
        return (registry, script, target);
    }

    private static Script NewScript() => new(CoreModules.GlobalConsts | CoreModules.Basic | CoreModules.ErrorHandling |
        CoreModules.String | CoreModules.Math | CoreModules.Table)
    {
        ExecutionBudget = new ExecutionBudget(1_000_000, TimeSpan.FromSeconds(5))
    };

    [ScriptType]
    public sealed class EnumConsumer
    {
        private EnumTestColor _color = EnumTestColor.Red;
        public int SetterCalls { get; private set; }
        [ScriptVisible(AllowWrite = true)]
        public EnumTestColor Color
        {
            get => _color;
            set { SetterCalls++; _color = value; }
        }
        [ScriptVisible(AllowWrite = true)] public EnumTestFlags Flags { get; set; }
        [ScriptVisible] public void SetColor(EnumTestColor color) => Color = color;
        [ScriptVisible] public EnumTestColor EchoColor(EnumTestColor color) => color;
        [ScriptVisible] public EnumTestFlags EchoFlags(EnumTestFlags flags) => flags;
        [ScriptVisible] public EnumUnnamedZeroFlags EchoUnnamedFlags(EnumUnnamedZeroFlags flags) => flags;
    }

    [ScriptType]
    public sealed class EnumSnapshotRoot { [ScriptVisible] public EnumSnapshotChild Child { get; } = new(); }

    [ScriptType]
    public sealed class EnumSnapshotChild
    {
        public EnumTestColor ColorValue = EnumTestColor.Green;
        public EnumTestFlags FlagsValue = EnumTestFlags.High;
        [ScriptVisible] public EnumTestColor Color => ColorValue;
        [ScriptVisible] public EnumTestFlags Flags => FlagsValue;
    }

    private enum PrivateEnum { Hidden }
}

public enum EnumTestColor { Red = 1, Scarlet = Red, Green = 2, Blue = 3 }

[Flags]
public enum EnumTestFlags : ulong { None = 0, Low = 1, High = 1UL << 63 }

[Flags]
public enum EnumUnnamedZeroFlags { Low = 1, High = 2 }

[Flags]
public enum EnumSignedFlags : long { Low = 1, Sign = long.MinValue, All = -1 }

[Flags]
public enum EnumCompositeFlags { A = 3, B = 5 }

public sealed class EnumOwnerA
{
    public enum Mode { First }
    public static string Sensitive => throw new InvalidOperationException("Do not expose enum owners.");
}

public sealed class EnumOwnerB { public enum Mode { Second } }
public sealed class GenericEnumOwner<T> { public enum Mode { Generic } }
