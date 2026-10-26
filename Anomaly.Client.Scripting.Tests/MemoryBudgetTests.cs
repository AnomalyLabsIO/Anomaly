using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class MemoryBudgetTests
{
    [Fact]
    public void Failed_reservation_is_atomic_and_stays_terminal()
    {
        var budget = new MemoryBudget(100);
        using (budget.EnterScope())
        {
            MemoryBudget.Reserve(80);
            var error = Assert.Throws<ScriptExecutionAbortedException>(() => MemoryBudget.Reserve(21));
            Assert.Equal(ExecutionTerminationReason.MemoryLimit, error.Reason);
            Assert.Equal(80, budget.ChargedBytes);
            Assert.True(budget.IsTerminated);
            Assert.Equal(ExecutionTerminationReason.MemoryLimit, Assert.Throws<ScriptExecutionAbortedException>(() => MemoryBudget.Reserve(0)).Reason);
            Assert.Equal(80, budget.ChargedBytes);
        }
        Assert.Equal(ExecutionTerminationReason.MemoryLimit, Assert.Throws<ScriptExecutionAbortedException>(budget.Check).Reason);
        Assert.Throws<ScriptExecutionAbortedException>(() => budget.EnterScope());
    }

    [Fact]
    public void Long_maximum_limit_does_not_wrap_when_the_next_reservation_overflows()
    {
        var budget = new MemoryBudget(long.MaxValue);
        using (budget.EnterScope())
        {
            MemoryBudget.Reserve(long.MaxValue - 5);
            MemoryBudget.Reserve(5);
            Assert.Equal(long.MaxValue, budget.ChargedBytes);
            Assert.Throws<ScriptExecutionAbortedException>(() => MemoryBudget.Reserve(1));
            Assert.Equal(long.MaxValue, budget.ChargedBytes);
        }
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void Impossible_string_lengths_abort_before_arithmetic_or_allocation(long characters)
    {
        var budget = new MemoryBudget(long.MaxValue);
        using (budget.EnterScope())
        {
            var error = Assert.Throws<ScriptExecutionAbortedException>(() => MemoryBudget.ReserveString(characters));
            Assert.Equal(ExecutionTerminationReason.MemoryLimit, error.Reason);
            Assert.Equal(0, budget.ChargedBytes);
        }
    }

    [Fact]
    public void Nested_ambient_scopes_restore_the_previous_budget_even_after_failure()
    {
        var previous = MemoryBudget.Current;
        var outer = new MemoryBudget(100);
        var inner = new MemoryBudget(10);
        using (outer.EnterScope())
        {
            MemoryBudget.Reserve(3);
            Assert.Throws<ScriptExecutionAbortedException>(() =>
            {
                using (inner.EnterScope()) MemoryBudget.Reserve(11);
            });
            Assert.Same(outer, MemoryBudget.Current);
            MemoryBudget.Reserve(4);
            Assert.Equal(7, outer.ChargedBytes);
            Assert.Equal(0, inner.ChargedBytes);
        }
        Assert.Same(previous, MemoryBudget.Current);
    }

    [Fact]
    public void Ambient_scopes_do_not_flow_to_another_thread()
    {
        var outer = new MemoryBudget(100);
        var inner = new MemoryBudget(100);
        Exception? failure = null;
        using (outer.EnterScope())
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.Null(MemoryBudget.Current);
                    using (inner.EnterScope()) MemoryBudget.Reserve(9);
                    Assert.Null(MemoryBudget.Current);
                }
                catch (Exception error) { failure = error; }
            });
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
            Assert.Null(failure);
            Assert.Same(outer, MemoryBudget.Current);
            MemoryBudget.Reserve(5);
        }
        Assert.Equal(5, outer.ChargedBytes);
        Assert.Equal(9, inner.ChargedBytes);
    }

    [Fact]
    public void Accounting_has_no_gc_or_scope_refund()
    {
        var budget = new MemoryBudget(1000);
        using (budget.EnterScope()) MemoryBudget.ReserveString(100);
        var first = budget.ChargedBytes;
        using (budget.EnterScope()) MemoryBudget.ReserveString(100);
        Assert.Equal(232, first);
        Assert.Equal(464, budget.ChargedBytes);
    }
}
