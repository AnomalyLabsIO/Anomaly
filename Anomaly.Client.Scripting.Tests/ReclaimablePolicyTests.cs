using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ReclaimablePolicyTests
{
    [Fact]
    public void Legacy_engine_callers_keep_the_explicit_cumulative_default()
    {
        var budget = new MemoryBudget(100);
        Assert.False(budget.IsReclaimable);
        budget.Charge(80);
        Assert.Equal(0, budget.Sweep());
        Assert.Equal(80, budget.ChargedBytes);
        Assert.Equal(80, budget.TotalAllocatedBytes);
        Assert.Equal(80, budget.PeakBytes);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Parent_and_child_cannot_mix_incompatible_accounting_modes(bool parentMode, bool childMode)
    {
        var parent = new MemoryBudget(4096, reclaimable: parentMode);
        Assert.Throws<ArgumentException>(() => new MemoryBudget(4096, parent, reclaimable: childMode));
        Assert.Equal(0, parent.ChargedBytes);
        Assert.False(parent.IsTerminated);
    }

    [Fact]
    public void Sweep_rejects_unbounded_work_and_never_resets_a_terminal_quota()
    {
        var budget = new MemoryBudget(1024, reclaimable: true);
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Sweep(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Sweep(int.MaxValue));
        Assert.Equal(0, budget.Sweep(0));
        Assert.Throws<ScriptExecutionAbortedException>(() => budget.ReserveAllocation(2048));
        Assert.Equal(0, budget.Sweep());
        Assert.True(budget.IsTerminated);
        Assert.Equal(ExecutionTerminationReason.MemoryLimit,
            Assert.Throws<ScriptExecutionAbortedException>(budget.Check).Reason);
    }
}
