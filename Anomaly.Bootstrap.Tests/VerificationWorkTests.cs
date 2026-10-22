using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class VerificationWorkTests
{
    [Fact]
    public void Fast_work_stops_after_32_individual_records()
    {
        var budget = new GuardWorkBudget(() => 0, 100);
        int records = 0;
        while (budget.CanStartItem()) { ++records; budget.CompleteItem(); }
        Assert.Equal(32, records);
        Assert.Equal(0, budget.Overruns);
    }

    [Fact]
    public void Elapsed_budget_stops_before_the_next_item()
    {
        long clock = 0;
        var budget = new GuardWorkBudget(() => clock, 100);
        Assert.True(budget.CanStartItem());
        clock = 100;
        budget.CompleteItem();
        Assert.False(budget.CanStartItem());
        Assert.Equal(1, budget.Items);
    }

    [Fact]
    public void An_indivisible_overrun_is_recorded_without_starting_more_work()
    {
        long clock = 0;
        var budget = new GuardWorkBudget(() => clock, 100);
        Assert.True(budget.CanStartItem());
        clock = 101;
        budget.CompleteItem();
        Assert.Equal(1, budget.Overruns);
        Assert.False(budget.CanStartItem());
    }

    [Fact]
    public void A_target_changed_during_its_lease_is_requeued()
    {
        var work = new RevisionWorkQueue<string>(2);
        Assert.True(work.Enqueue("a"));
        Assert.True(work.TryTake(out string target, out long before));
        Assert.True(work.Enqueue(target));
        Assert.False(work.IsCurrent(target, before));
        work.Complete(target, before);
        Assert.Equal(1, work.Count);
        Assert.True(work.TryTake(out _, out long after));
        Assert.True(after > before);
        work.Complete(target, after);
        Assert.Equal(0, work.Count);
    }

    [Fact]
    public void Repeated_changes_to_one_target_do_not_duplicate_ready_items()
    {
        var work = new RevisionWorkQueue<string>(2);
        for (int i = 0; i < 1000; ++i) Assert.True(work.Enqueue("a"));
        Assert.Equal(1, work.Count);
        Assert.True(work.TryTake(out string target, out long version));
        Assert.False(work.TryTake(out _, out _));
        work.Complete(target, version);
        Assert.Equal(0, work.Count);
    }

    [Fact]
    public void Capacity_counts_leased_targets_and_allows_updates_to_existing_ones()
    {
        var work = new RevisionWorkQueue<string>(1);
        Assert.True(work.Enqueue("a"));
        Assert.True(work.TryTake(out _, out long version));
        Assert.False(work.Enqueue("b"));
        Assert.True(work.Enqueue("a"));
        work.Complete("a", version);
        Assert.True(work.TryTake(out _, out version));
        work.Complete("a", version);
        Assert.True(work.Enqueue("b"));
    }

    [Fact]
    public void Concurrent_mutations_cannot_be_published_as_the_leased_revision()
    {
        var work = new RevisionWorkQueue<string>(1);
        work.Enqueue("a");
        work.TryTake(out _, out long version);
        Parallel.For(0, 100, _ => Assert.True(work.Enqueue("a")));
        Assert.False(work.IsCurrent("a", version));
        work.Complete("a", version);
        Assert.True(work.TryTake(out _, out long current));
        Assert.Equal(101, current);
        work.Complete("a", current);
        Assert.Equal(0, work.Count);
    }
}
