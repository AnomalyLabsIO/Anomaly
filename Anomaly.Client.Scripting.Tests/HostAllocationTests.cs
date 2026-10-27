using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Bindings;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class HostAllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Primitive_argument_boxes_are_reserved_before_conversion(bool boolean)
    {
        var registry = new ScriptBindingRegistry();
        // Attach a deliberately tiny allowance after trusted VM construction to
        // isolate this host conversion, rather than testing VM initialization.
        var script = new Script(CoreModules.None) { MemoryBudget = new MemoryBudget(32, reclaimable: true) };
        var value = boolean ? DynValue.True : DynValue.NewNumber(42);
        var failure = Assert.Throws<ScriptExecutionAbortedException>(() =>
            registry.FromScriptValue(script, value, boolean ? typeof(bool) : typeof(int)));
        Assert.Equal(ExecutionTerminationReason.MemoryLimit, failure.Reason);
        Assert.Equal(0, script.MemoryBudget.UnboundBytes);
        Assert.Null(MemoryBudget.Current);
    }

    [Fact]
    public void Reflection_workspace_is_reserved_before_entering_the_approved_method()
    {
        var target = new AllocationFixture();
        var script = new Script(CoreModules.None) { MemoryBudget = new MemoryBudget(1024, reclaimable: true) };
        var failure = Assert.Throws<ScriptExecutionAbortedException>(() => ScriptBindingRegistry.Invoke(script,
            typeof(AllocationFixture).GetMethod(nameof(AllocationFixture.Touch))!, target, null));
        Assert.Equal(ExecutionTerminationReason.MemoryLimit, failure.Reason);
        Assert.Equal(0, target.Calls);
        Assert.Equal(0, script.MemoryBudget.UnboundBytes);
        Assert.Null(MemoryBudget.Current);
    }

    [Fact]
    public void Binding_descriptors_are_reserved_before_exposing_host_references()
    {
        var registry = new ScriptBindingRegistry();
        registry.RegisterType<AllocationFixture>();
        var script = new Script(CoreModules.None) { MemoryBudget = new MemoryBudget(128, reclaimable: true) };
        var failure = Assert.Throws<ScriptExecutionAbortedException>(() => registry.ToScriptValue(script, new AllocationFixture()));
        Assert.Equal(ExecutionTerminationReason.MemoryLimit, failure.Reason);
        Assert.Equal(0, script.MemoryBudget.UnboundBytes);
        Assert.Null(MemoryBudget.Current);
    }

    [Fact]
    public void Host_resources_have_a_fixed_retained_collection_bound()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession();
        // The runtime installs one owned Output resource before source execution.
        var resources = Enumerable.Range(0, 31).Select(_ => new Resource()).ToArray();
        foreach (var resource in resources) session.TrackResource(resource);
        Assert.Throws<InvalidOperationException>(() => session.TrackResource(new Resource()));
        session.Dispose();
        Assert.All(resources, resource => Assert.Equal(1, resource.Disposals));
    }

    [Fact]
    public void Oversized_queued_host_strings_are_rejected_before_the_queue_retains_them()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession();
        Assert.True(session.Load("function event(value) error('must not run') end").Succeeded);
        Assert.Throws<ArgumentException>(() => session.QueueEvent("event", new string('x', 4097)));
        Assert.False(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.False(session.IsRetired);
        Assert.Equal(0, session.MemoryUnboundBytes);
    }

    [Fact]
    public async Task Retained_copies_of_script_arguments_keep_their_backing_storage_charged()
    {
        var result = await CpuProbeProcess.Run("hostalloc-retained-argument-strings");
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("MemoryLimit", result.Reason);
        Assert.True(result.MemoryTerminal);
        Assert.InRange(result.HostCalls, 1, 4095);
        Assert.Equal(0, result.UnboundBytes);
    }

    [Fact]
    public async Task Bounded_binding_lookup_conversion_and_invocation_churn_can_be_reclaimed()
    {
        var result = await CpuProbeProcess.Run("hostalloc-bounded-binding-churn");
        Assert.Equal("Completed", result.Outcome);
        Assert.Equal(2000, result.Entries);
        Assert.Equal(2000, result.HostCalls);
        Assert.Equal(0, result.UnboundBytes);
        Assert.InRange(result.MemoryChargedBytes, 1, result.MemoryLimitBytes);
    }

    [Fact]
    public async Task Retaining_only_the_bounded_diagnostic_string_keeps_its_allowance()
    {
        var result = await CpuProbeProcess.Run("hostalloc-error-message-retention");
        Assert.Equal("Completed", result.Outcome);
        Assert.Equal(2, result.Checks);
        Assert.True(result.Number >= 4096);
        Assert.Equal(0, result.UnboundBytes);
    }

    [Fact]
    public async Task Reentrant_snapshot_getters_retire_the_session_without_unbounded_clr_recursion()
    {
        var result = await CpuProbeProcess.Run("hostalloc-reentrant-snapshot-getter");
        Assert.Equal("Completed", result.Outcome);
        Assert.Equal(1, result.HostCalls);
        Assert.Equal(1, result.RejectedCalls);
        Assert.Equal(1, result.Checks);
    }

    [Fact]
    public async Task Approved_clr_method_bodies_remain_a_separate_allocation_trust_boundary()
    {
        // This finite fixture records a limitation, not a containment success:
        // the allowance cannot intercept a byte[] allocated inside arbitrary C#.
        var result = await CpuProbeProcess.Run("hostalloc-uncooperative-clr-body");
        Assert.Equal("Completed", result.Outcome);
        Assert.Equal(1, result.HostCalls);
        Assert.Equal(65536d, result.Number);
        Assert.True(result.AllocationDeltaBytes >= 65536);
        Assert.Equal(1, result.Checks);
        Assert.False(ScriptingPolicy.MemoryHardeningQualified);
    }

    [ScriptType]
    public sealed class AllocationFixture
    {
        public int Calls { get; private set; }
        [ScriptVisible] public int Touch() => ++Calls;
    }

    private sealed class Resource : IDisposable
    {
        internal int Disposals;
        public void Dispose() => Disposals++;
    }
}
