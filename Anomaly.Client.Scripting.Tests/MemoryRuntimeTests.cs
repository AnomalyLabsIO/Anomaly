using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class MemoryRuntimeTests
{
    [Fact]
    public void Child_budgets_share_a_sticky_parent_allowance()
    {
        var parent = new MemoryBudget(100);
        var first = new MemoryBudget(100, parent);
        var second = new MemoryBudget(100, parent);
        first.Charge(60);
        second.Charge(30);
        Assert.Equal(90, parent.ChargedBytes);
        Assert.Throws<ScriptExecutionAbortedException>(() => first.Charge(11));
        Assert.True(parent.IsTerminated);
        Assert.True(first.IsTerminated);
        Assert.True(second.IsTerminated);
        Assert.Throws<ScriptExecutionAbortedException>(() => second.Charge(0));
    }

    [Fact]
    public void Exhausting_one_child_does_not_poison_its_siblings()
    {
        var parent = new MemoryBudget(1000);
        var first = new MemoryBudget(10, parent);
        var second = new MemoryBudget(100, parent);
        Assert.Throws<ScriptExecutionAbortedException>(() => first.Charge(11));
        second.Charge(90);
        Assert.False(parent.IsTerminated);
        Assert.False(second.IsTerminated);
        Assert.Equal(90, parent.ChargedBytes);
    }

    [Fact]
    public void Revoking_sessions_does_not_refund_objects_that_have_not_been_collected()
    {
        using var runtime = new ScriptRuntime(allocationLimitBytes: 1024 * 1024);
        var payload = new string('x', 4096);
        using var session = runtime.CreateDevelopmentSession();
        session.SetGlobal("payload", payload);
        var outstanding = runtime.MemoryChargedBytes;
        var total = runtime.MemoryTotalAllocatedBytes;
        runtime.RevokeAll();
        Assert.True(session.IsRetired);
        Assert.Equal(outstanding, runtime.MemoryChargedBytes);
        Assert.Equal(total, runtime.MemoryTotalAllocatedBytes);
        using var second = runtime.CreateDevelopmentSession();
        Assert.True(runtime.MemoryTotalAllocatedBytes > total);
    }
}
