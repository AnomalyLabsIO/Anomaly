using System.Reflection;
using System.Reflection.Emit;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Bindings;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class DiscoverySecurityTests
{
    private static int _discoveryEffects;

    [Fact]
    public void Default_names_follow_the_actual_type_and_members_without_case_fallback()
    {
        var fixture = new FixtureAssembly();
        var type = fixture.Define("DiscoveryDefaults.DefaultTarget");
        var registry = new ScriptBindingRegistry();
        registry.AutoDiscoverScriptTypes(fixture.Assembly);

        var script = new Script(CoreModules.Preset_HardSandbox);
        registry.SetGlobal(script, "target", Activator.CreateInstance(type));
        Assert.True(script.DoString(@"
assert(target.Count == 7 and target:Add(2) == 3)
assert(target.count == nil and target.add == nil)
assert(target.Hidden == nil and target.GetType == nil and target.__new == nil)
return true").Boolean);
        var surface = registry.DescribeSurface();
        Assert.Contains("type DefaultTarget -> DiscoveryDefaults.DefaultTarget", surface);
        Assert.Contains("property Count:", surface);
        Assert.Contains("method Add(", surface);
    }

    [Fact]
    public void Explicit_aliases_override_defaults_without_exposing_the_original_members()
    {
        var fixture = new FixtureAssembly();
        var type = fixture.Define("DiscoveryAliases.OriginalTarget", typeAlias: "customTarget",
            propertyAlias: "count", methodAlias: "plus");
        var registry = new ScriptBindingRegistry();
        registry.AutoDiscoverScriptTypes(fixture.Assembly);
        var script = new Script(CoreModules.Preset_HardSandbox);
        registry.SetGlobal(script, "target", Activator.CreateInstance(type));

        Assert.True(script.DoString(@"
return target.count == 7 and target:plus(2) == 3
    and target.Count == nil and target.Add == nil").Boolean);
        Assert.Contains("type customTarget -> DiscoveryAliases.OriginalTarget", registry.DescribeSurface());
    }

    [Fact]
    public void Discovery_uses_only_the_assembly_explicitly_selected_by_the_trusted_host()
    {
        var first = new FixtureAssembly();
        first.Define("DiscoveryFirst.FirstTarget");
        var second = new FixtureAssembly();
        second.Define("DiscoverySecond.SecondTarget");
        var registry = new ScriptBindingRegistry();

        registry.AutoDiscoverScriptTypes(first.Assembly);
        Assert.Contains("type FirstTarget ->", registry.DescribeSurface());
        Assert.DoesNotContain("SecondTarget", registry.DescribeSurface());

        // Explicit host selection of another assembly is allowed; an AppDomain scan is not.
        registry.AutoDiscoverScriptTypes(second.Assembly);
        Assert.Contains("type SecondTarget ->", registry.DescribeSurface());
    }

    [Fact]
    public void Unannotated_and_explicitly_ignored_types_are_not_discovered()
    {
        var fixture = new FixtureAssembly();
        fixture.Define("DiscoveryFilter.ApprovedTarget");
        fixture.Define("DiscoveryFilter.UnannotatedTarget", annotated: false);
        fixture.Define("DiscoveryFilter.IgnoredTarget", ignored: true);
        var registry = new ScriptBindingRegistry();

        registry.AutoDiscoverScriptTypes(fixture.Assembly);

        var surface = registry.DescribeSurface();
        Assert.Contains("type ApprovedTarget ->", surface);
        Assert.DoesNotContain("UnannotatedTarget", surface);
        Assert.DoesNotContain("IgnoredTarget", surface);
    }

    [Fact]
    public void Discovering_and_exporting_metadata_does_not_instantiate_bound_types_or_read_properties()
    {
        _discoveryEffects = 0;
        var fixture = new FixtureAssembly();
        fixture.Define("DiscoveryEffects.EffectTarget", effects: true);
        var registry = new ScriptBindingRegistry();

        registry.AutoDiscoverScriptTypes(fixture.Assembly);
        Assert.Contains("EffectTarget", registry.DescribeSurface());
        Assert.Contains("EffectTarget", registry.GetCatalog().ToJson());

        // Constructors, type initializers and annotated getters all call this witness.
        Assert.Equal(0, _discoveryEffects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_discovery_batch_with_colliding_type_aliases_is_atomic(bool explicitAlias)
    {
        var fixture = new FixtureAssembly();
        var existing = fixture.Define("DiscoveryCollision.ExistingTarget", typeAlias: "existingTarget");
        fixture.Define("DiscoveryCollisionA.DuplicateTarget", typeAlias: explicitAlias ? "duplicate" : null);
        fixture.Define("DiscoveryCollisionB.DuplicateTarget", typeAlias: explicitAlias ? "duplicate" : null);
        var registry = new ScriptBindingRegistry();
        registry.RegisterType(existing);
        var before = registry.DescribeSurface();

        Assert.Throws<InvalidOperationException>(() => registry.AutoDiscoverScriptTypes(fixture.Assembly));

        Assert.Equal(before, registry.DescribeSurface());
    }

    [Theory]
    [InlineData("__index")]
    [InlineData("end")]
    [InlineData("return")]
    [InlineData("with.dot")]
    [InlineData("bad-name")]
    [InlineData("\u03a9")]
    [InlineData("")]
    [InlineData(" ")]
    public void Invalid_type_aliases_reject_the_whole_discovery_batch(string alias)
    {
        var fixture = new FixtureAssembly();
        fixture.Define("DiscoveryInvalid.AFirstValidTarget");
        fixture.Define("DiscoveryInvalid.ZInvalidTarget", typeAlias: alias);
        var registry = new ScriptBindingRegistry();

        Assert.NotNull(Record.Exception(() => registry.AutoDiscoverScriptTypes(fixture.Assembly)));

        Assert.Equal("", registry.DescribeSurface());
    }

    [Theory]
    [InlineData("end", null)]
    [InlineData("Count", "return")]
    [InlineData("Count", "__metatable")]
    [InlineData("Count", "")]
    public void Invalid_default_or_explicit_member_names_do_not_publish_a_partial_policy(string name, string? alias)
    {
        var fixture = new FixtureAssembly();
        fixture.Define("DiscoveryMembers.AFirstValidTarget");
        fixture.Define("DiscoveryMembers.ZInvalidMemberTarget", propertyName: name, propertyAlias: alias);
        var registry = new ScriptBindingRegistry();

        Assert.NotNull(Record.Exception(() => registry.AutoDiscoverScriptTypes(fixture.Assembly)));

        Assert.Equal("", registry.DescribeSurface());
    }

    [Theory]
    [InlineData(typeof(object))]
    [InlineData(typeof(Type))]
    [InlineData(typeof(Delegate))]
    [InlineData(typeof(Task))]
    [InlineData(typeof(List<int>))]
    public void Discovery_applies_the_same_closed_return_graph_policy_as_explicit_registration(Type unsafeType)
    {
        var fixture = new FixtureAssembly();
        fixture.Define("DiscoveryGraph.AFirstValidTarget");
        fixture.Define("DiscoveryGraph.ZUnsafeTarget", propertyType: unsafeType);
        var registry = new ScriptBindingRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.AutoDiscoverScriptTypes(fixture.Assembly));

        Assert.Equal("", registry.DescribeSurface());
    }

    // Public so the isolated emitted fixture assembly can invoke this witness.
#pragma warning disable xUnit1013
    public static void MarkDiscoveryEffect() => Interlocked.Increment(ref _discoveryEffects);
#pragma warning restore xUnit1013

    // Each test needs an isolated assembly: the main test assembly deliberately contains
    // annotated unsafe fixtures for explicit registration tests and must never be scanned wholesale.
    private sealed class FixtureAssembly
    {
        private readonly ModuleBuilder _module;

        internal FixtureAssembly()
        {
            Assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("Anomaly.ScriptDiscoveryFixture." + Guid.NewGuid().ToString("N")),
                AssemblyBuilderAccess.RunAndCollect);
            _module = Assembly.DefineDynamicModule("Fixtures");
        }

        internal AssemblyBuilder Assembly { get; }

        internal Type Define(string fullName, string? typeAlias = null, string propertyName = "Count",
            string? propertyAlias = null, string? methodAlias = null, bool annotated = true,
            bool ignored = false, bool effects = false, Type? propertyType = null)
        {
            var type = _module.DefineType(fullName, TypeAttributes.Public | TypeAttributes.Sealed);
            if (annotated) type.SetCustomAttribute(Attribute(typeof(ScriptTypeAttribute), typeAlias));
            if (ignored)
                type.SetCustomAttribute(new CustomAttributeBuilder(typeof(ScriptIgnoreAttribute).GetConstructor(Type.EmptyTypes)!, Array.Empty<object>()));

            var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            if (effects) Mark(il);
            il.Emit(OpCodes.Ret);
            if (effects)
            {
                il = type.DefineTypeInitializer().GetILGenerator();
                Mark(il);
                il.Emit(OpCodes.Ret);
            }

            var resultType = propertyType ?? typeof(int);
            var property = type.DefineProperty(propertyName, PropertyAttributes.None, resultType, Type.EmptyTypes);
            var getter = type.DefineMethod("get_" + propertyName,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                resultType, Type.EmptyTypes);
            il = getter.GetILGenerator();
            if (effects) Mark(il);
            if (resultType == typeof(int)) il.Emit(OpCodes.Ldc_I4_7);
            else il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);
            property.SetCustomAttribute(Attribute(typeof(ScriptVisibleAttribute), propertyAlias));

            var method = type.DefineMethod("Add", MethodAttributes.Public | MethodAttributes.HideBySig,
                typeof(int), new[] { typeof(int) });
            il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ret);
            method.SetCustomAttribute(Attribute(typeof(ScriptVisibleAttribute), methodAlias));

            var hidden = type.DefineMethod("Hidden", MethodAttributes.Public | MethodAttributes.HideBySig,
                typeof(string), Type.EmptyTypes);
            il = hidden.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "not exposed");
            il.Emit(OpCodes.Ret);
            return type.CreateType()!;
        }

        private static CustomAttributeBuilder Attribute(Type attribute, string? alias)
            => new(attribute.GetConstructor(new[] { typeof(string) })!, new object?[] { alias });

        private static void Mark(ILGenerator il)
            => il.Emit(OpCodes.Call, typeof(DiscoverySecurityTests).GetMethod(nameof(MarkDiscoveryEffect))!);
    }
}
