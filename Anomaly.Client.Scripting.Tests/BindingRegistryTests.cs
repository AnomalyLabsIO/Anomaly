using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Bindings;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class BindingRegistryTests
{
    [Fact]
    public void Only_selected_declared_members_and_aliases_are_visible()
    {
        var (registry, script, target) = BoundSample();
        Assert.True(script.DoString(@"
assert(sample.id == 7 and sample.name == 'example')
assert(sample.Id == nil and sample.Raw == nil and sample.Field == nil)
assert(sample.Changed == nil and sample.inherited == nil)
assert(sample.hidden == nil and sample.GetType == nil and sample.Equals == nil)
assert(sample.new == nil and sample.__new == nil)
return tostring(sample) == '[script sample]'").Boolean);
        Assert.Equal("example", target.Name);
        Assert.DoesNotContain("GetType", registry.DescribeSurface());
        Assert.DoesNotContain("inherited", registry.DescribeSurface());
    }

    [Fact]
    public void Properties_are_readonly_unless_the_setter_is_explicitly_enabled()
    {
        var (_, script, target) = BoundSample();
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("sample.id = 99"));
        Assert.Equal(7, target.Id);
        script.DoString("sample.label = 'changed'; sample.active = true");
        Assert.Equal("changed", target.Label);
        Assert.True(target.Active);
    }

    [Fact]
    public void Ignore_overrides_visibility_and_explicit_write_selection()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<IgnoredSetter>();
        var script = NewScript();
        registry.SetGlobal(script, "target", new IgnoredSetter());
        Assert.Equal(12d, script.DoString("return target.value").Number);
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("target.value = 13"));
        Assert.Contains("property value: System.Int32 readonly", registry.DescribeSurface());
        Assert.Throws<InvalidOperationException>(() => registry.RegisterType<IgnoredType>());
    }

    [Fact]
    public void Methods_accept_colon_and_detached_calls_without_receiver_spoofing()
    {
        var (_, script, _) = BoundSample();
        Assert.Equal(5d, script.DoString("return sample:add(2, 3)").Number);
        Assert.Equal(9d, script.DoString("local add = sample.add; return add(4, 5)").Number);
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("local other = { add = sample.add }; return other:add(1, 2)"));
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return sample:add(1)"));
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return sample:add(1, 2, 3)"));
    }

    [Theory]
    [InlineData("sample:add('1', 2)")]
    [InlineData("sample:add(true, 2)")]
    [InlineData("sample:add(1.5, 2)")]
    [InlineData("sample:add(0/0, 2)")]
    [InlineData("sample:add(1/0, 2)")]
    [InlineData("sample:add(2147483648, 2)")]
    [InlineData("sample.label = {}")]
    [InlineData("sample.active = 1")]
    public void Scalar_arguments_are_strict_and_invalid_writes_are_atomic(string source)
    {
        var (_, script, target) = BoundSample();
        Assert.Throws<ScriptRuntimeException>(() => script.DoString(source));
        Assert.Equal("original", target.Label);
        Assert.False(target.Active);
    }

    [Fact]
    public void Strings_are_bounded_in_both_directions_in_utf8_bytes()
    {
        var (_, script, target) = BoundSample();
        script.DoString("sample.label = string.rep('x', 4096)");
        Assert.Equal(4096, target.Label.Length);
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("sample.label = string.rep('x', 4097)"));
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("sample.label = string.rep('é', 2049)"));
        Assert.Equal(4096, target.Label.Length);
        target.Name = new string('é', 2049);
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return sample.name"));
    }

    [Fact]
    public void Annotated_nested_types_are_checked_and_never_fall_back_to_raw_userdata()
    {
        var (_, script, _) = BoundSample();
        Assert.Equal(3d, script.DoString("return sample.child.value").Number);
        Assert.True(script.DoString("return sample.child.Raw == nil and sample.child.GetType == nil").Boolean);
    }

    [Theory]
    [InlineData(typeof(UnsafeObjectReturn))]
    [InlineData(typeof(UnsafeTaskReturn))]
    [InlineData(typeof(UnsafeDelegateReturn))]
    [InlineData(typeof(UnsafeCollectionReturn))]
    [InlineData(typeof(UnsafeIndex))]
    [InlineData(typeof(UnsafeGenericMethod))]
    [InlineData(typeof(UnsafeStaticMethod))]
    [InlineData(typeof(UnsafePrivateSetter))]
    public void Unsupported_annotated_graphs_are_rejected_before_registration(Type type)
    {
        var registry = new ScriptBindingRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.RegisterType(type));
        Assert.Equal("", registry.DescribeSurface());
    }

    [Fact]
    public void Unregistered_globals_and_unannotated_subclasses_are_rejected()
    {
        var registry = new ScriptBindingRegistry();
        var script = NewScript();
        Assert.Throws<ScriptRuntimeException>(() => registry.SetGlobal(script, "target", new Sample()));
        registry.RegisterType<Sample>();
        Assert.Throws<ScriptRuntimeException>(() => registry.SetGlobal(script, "target", new DerivedSample()));
        Assert.Throws<InvalidOperationException>(() => registry.RegisterType<DerivedSample>());
        Assert.Equal(DataType.Nil, script.Globals.Get("target").Type);
    }

    [Fact]
    public void Runtime_subclasses_cannot_expand_an_approved_nested_return_type()
    {
        var (registry, script, target) = BoundSample();
        target.Child = new DerivedChild();
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return sample.child"));
        Assert.DoesNotContain("dangerous", registry.DescribeSurface());
    }

    [Fact]
    public void Cyclic_object_access_has_a_bounded_depth()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<Chain>();
        var script = NewScript();
        registry.SetGlobal(script, "node", new Chain());
        Assert.Throws<ScriptRuntimeException>(() => script.DoString(
            $"local node = node; for i = 1, {ScriptBindingRegistry.MaxObjectDepth + 1} do node = node.next end"));
    }

    [Fact]
    public void Userdata_copied_by_a_host_cannot_be_used_in_another_script()
    {
        var (registry, first, _) = BoundSample();
        var second = NewScript();
        second.Globals.Set("sample", first.Globals.Get("sample"));
        Assert.Throws<ScriptRuntimeException>(() => second.DoString("return sample.id"));
        registry.SetGlobal(second, "sample", new Sample());
        Assert.Equal(7d, second.DoString("return sample.id").Number);
    }

    [Fact]
    public void Method_reference_arguments_must_belong_to_the_current_binding_graph()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<ChildConsumer>();
        var script = NewScript();
        registry.SetGlobal(script, "consumer", new ChildConsumer());
        registry.SetGlobal(script, "child", new Child());
        Assert.Equal(3d, script.DoString("return consumer:read(child)").Number);
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return consumer:read({ value = 3 })"));
        var otherRegistry = new ScriptBindingRegistry();
        otherRegistry.RegisterType<Child>();
        otherRegistry.SetGlobal(script, "other", new Child());
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return consumer:read(other)"));
    }

    [Fact]
    public void Current_budget_is_checked_dynamically_before_a_getter_runs()
    {
        var (registry, script, target) = BoundSample();
        var value = script.Globals.Get("sample");
        script.ExecutionBudget = new ExecutionBudget(1000, TimeSpan.FromSeconds(5));
        script.ExecutionBudget.Cancel();
        var error = Assert.Throws<ScriptExecutionAbortedException>(() =>
            value.UserData.Descriptor.Index(script, target, DynValue.NewString("name"), true));
        Assert.Equal(ExecutionTerminationReason.Cancelled, error.Reason);
        script.ExecutionBudget = null;
        registry.SetGlobal(script, "sample", target);
        Assert.Equal("example", script.DoString("return sample.name").String);
    }

    [Fact]
    public void Terminal_abort_unwrapped_from_reflection_cannot_be_caught_by_pcall()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<AbortTarget>();
        var script = NewScript();
        registry.SetGlobal(script, "target", new AbortTarget());
        var error = Assert.Throws<ScriptExecutionAbortedException>(() =>
            script.DoString("pcall(function() target:abort() end)"));
        Assert.Equal(ExecutionTerminationReason.Cancelled, error.Reason);
    }

    [Fact]
    public void A_host_cancelling_the_budget_cannot_return_success_to_lua()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<CancelTarget>();
        var script = NewScript();
        registry.SetGlobal(script, "target", new CancelTarget(() => script.ExecutionBudget!.Cancel()));
        Assert.Throws<ScriptExecutionAbortedException>(() => script.DoString("pcall(function() target:cancel() end)"));
        Assert.True(script.ExecutionBudget!.IsTerminated);
    }

    [Fact]
    public void Registration_is_instance_owned_and_surface_export_is_deterministic()
    {
        var first = new ScriptBindingRegistry();
        var second = new ScriptBindingRegistry();
        first.RegisterType<Sample>();
        second.RegisterType<Child>();
        second.RegisterType<Sample>();
        Assert.Equal(first.DescribeSurface(), second.DescribeSurface());
        Assert.Contains("property id: System.Int32 readonly", first.DescribeSurface());
        Assert.Contains("property label: System.String readwrite", first.DescribeSurface());
        Assert.Null(UserData.GetDescriptorForType(typeof(Sample), searchInterfaces: false));
        Assert.Null(UserData.Create(new Sample()));
    }

    [Fact]
    public void Duplicate_aliases_fail_without_partially_committing_the_graph()
    {
        var registry = new ScriptBindingRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.RegisterType<DuplicateAliasParent>());
        Assert.Equal("", registry.DescribeSurface());
    }

    [Fact]
    public void Client_ready_arguments_have_only_the_selected_safe_scene_name()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<ClientReadyEventArgs>();
        var script = NewScript();
        registry.SetGlobal(script, "ready", new ClientReadyEventArgs("Facility"));
        Assert.True(script.DoString("return ready.SceneName == 'Facility' and ready.scene_name == nil and ready.GetType == nil").Boolean);
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("ready.SceneName = 'changed'"));
    }

    [Fact]
    public void An_escaped_csharp_keyword_member_requires_an_explicit_valid_alias()
    {
        var registry = new ScriptBindingRegistry();
        Assert.Throws<ArgumentException>(() => registry.RegisterType<KeywordMember>());
        Assert.Equal("", registry.DescribeSurface());
        registry.RegisterType<AliasedKeywordMember>();
        var script = NewScript();
        registry.SetGlobal(script, "target", new AliasedKeywordMember());
        Assert.Equal(7d, script.DoString("return target.End").Number);
        Assert.True(script.DoString("return target['end'] == nil").Boolean);
    }

    private static (ScriptBindingRegistry Registry, Script Script, Sample Target) BoundSample()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<Sample>();
        var script = NewScript();
        var target = new Sample();
        registry.SetGlobal(script, "sample", target);
        return (registry, script, target);
    }

    private static Script NewScript() => new(CoreModules.GlobalConsts | CoreModules.Basic | CoreModules.ErrorHandling |
        CoreModules.String | CoreModules.Math | CoreModules.Table)
    {
        ExecutionBudget = new ExecutionBudget(1000000, TimeSpan.FromSeconds(5))
    };

    public class InheritedApi
    {
        [ScriptVisible("inherited")]
        public int Inherited => 1;
    }

    [ScriptType("sample")]
    public class Sample : InheritedApi
    {
        [ScriptVisible("id")] public int Id { get; set; } = 7;
        [ScriptVisible("name")] public string Name { get; set; } = "example";
        [ScriptVisible("label", AllowWrite = true)] public string Label { get; set; } = "original";
        [ScriptVisible("active", AllowWrite = true)] public bool Active { get; set; }
        [ScriptVisible("child")] public Child Child { get; set; } = new();
        [ScriptVisible("hidden"), ScriptIgnore] public object Hidden => new FakeUnityObject();
        [ScriptVisible("add")] public int Add(int left, int right) => left + right;
        public object Raw => new FakeUnityObject();
        public int Field = 17;
        public event Action Changed { add { } remove { } }
        public override string ToString() => throw new InvalidOperationException("Do not invoke a host ToString override.");
    }

    public sealed class DerivedSample : Sample { }
    public sealed class FakeUnityObject { public Type GetRawType() => GetType(); }

    [ScriptType("child")]
    public class Child
    {
        [ScriptVisible("value")] public int Value => 3;
        public object Raw => new FakeUnityObject();
    }

    public sealed class DerivedChild : Child { public string dangerous => "hidden"; }
    [ScriptType("consumer")]
    public sealed class ChildConsumer { [ScriptVisible("read")] public int Read(Child child) => child.Value; }
    [ScriptType("chain")]
    public sealed class Chain { [ScriptVisible("next")] public Chain Next => this; }
    [ScriptType("ignored_setter")]
    public sealed class IgnoredSetter
    {
        [ScriptVisible("value", AllowWrite = true)]
        public int Value { get; [ScriptIgnore] set; } = 12;
    }
    [ScriptType("ignored"), ScriptIgnore] public sealed class IgnoredType { }
    [ScriptType("unsafe_object")] public sealed class UnsafeObjectReturn { [ScriptVisible] public object Raw => new FakeUnityObject(); }
    [ScriptType("unsafe_task")] public sealed class UnsafeTaskReturn { [ScriptVisible] public Task Run() => Task.CompletedTask; }
    [ScriptType("unsafe_delegate")] public sealed class UnsafeDelegateReturn { [ScriptVisible] public Action Callback => () => { }; }
    [ScriptType("unsafe_list")] public sealed class UnsafeCollectionReturn { [ScriptVisible] public List<string> Values => new(); }
    [ScriptType("unsafe_index")] public sealed class UnsafeIndex { [ScriptVisible] public int this[int index] => index; }
    [ScriptType("unsafe_generic")] public sealed class UnsafeGenericMethod { [ScriptVisible] public T Get<T>(T value) => value; }
    [ScriptType("unsafe_static")] public sealed class UnsafeStaticMethod { [ScriptVisible] public static int Get() => 1; }
    [ScriptType("unsafe_setter")] public sealed class UnsafePrivateSetter { [ScriptVisible(AllowWrite = true)] public int Value { get; private set; } }
    [ScriptType("abort_target")]
    public sealed class AbortTarget
    {
        [ScriptVisible("abort")]
        public void Abort() => throw new ScriptExecutionAbortedException(ExecutionTerminationReason.Cancelled);
    }
    [ScriptType("cancel_target")]
    public sealed class CancelTarget
    {
        private readonly Action _cancel;
        public CancelTarget(Action cancel) => _cancel = cancel;
        [ScriptVisible("cancel")] public void Cancel() => _cancel();
    }
    [ScriptType("duplicate")]
    public sealed class DuplicateAliasParent { [ScriptVisible] public DuplicateAliasChild Child => new(); }
    [ScriptType("duplicate")]
    public sealed class DuplicateAliasChild { [ScriptVisible] public int Value => 1; }
    [ScriptType]
    public sealed class KeywordMember { [ScriptVisible] public int @end => 7; }
    [ScriptType]
    public sealed class AliasedKeywordMember { [ScriptVisible("End")] public int @end => 7; }
}
