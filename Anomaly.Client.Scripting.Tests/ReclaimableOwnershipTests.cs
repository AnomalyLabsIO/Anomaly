using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ReclaimableOwnershipTests
{
    [Fact]
    public async Task Keeping_only_a_nested_event_value_retains_its_publication_until_collection()
    {
        var result = await CpuProbeProcess.Run("reclaimable-nested-event-snapshot");
        Assert.Equal("Completed", result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(5, result.Checks);
        Assert.Equal(1, result.Entries);
        Assert.True(result.Number >= 64 * 1024);
    }

    [Fact]
    public async Task Repeated_noop_event_removal_does_not_charge_an_immortal_empty_array()
    {
        var result = await CpuProbeProcess.Run("reclaimable-noop-event-remove");
        Assert.Equal("Completed", result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(10000, result.Entries);
        Assert.Equal(10001, result.Checks);
        Assert.Equal(0, result.UnboundBytes);
    }

    [Theory]
    [InlineData("reclaimable-unbound-rollback", 2000)]
    [InlineData("reclaimable-throwing-factory", 1)]
    [InlineData("reclaimable-merged-owner-tickets", 2)]
    [InlineData("reclaimable-sweep-observation-only", 66)]
    [InlineData("reclaimable-bound-dispose", 6)]
    [InlineData("reclaimable-abandoned-child-retained-owner", 2)]
    [InlineData("reclaimable-shared-string-clone", 2)]
    [InlineData("reclaimable-cleared-table-capacity", 2)]
    [InlineData("reclaimable-pinned-owner", 2)]
    [InlineData("reclaimable-resurrected-owner", 2)]
    public async Task Storage_ownership_controls_credit_rather_than_ticket_or_wrapper_lifetime(string fixture, int checks)
    {
        var result = await CpuProbeProcess.Run(fixture);
        Assert.Equal("Completed", result.Outcome);
        Assert.Null(result.Error);
        Assert.False(result.MemoryTerminal);
        Assert.Equal(checks, result.Checks);
        Assert.Equal(0, result.MemoryChargedBytes);
        Assert.InRange(result.PeakBytes, 1, result.MemoryLimitBytes);
    }

    [Fact]
    public async Task Parent_exhaustion_terminates_only_the_allocating_child_and_the_pool_remains_reusable()
    {
        var result = await CpuProbeProcess.Run("reclaimable-parent-failure-reusable");
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("MemoryLimit", result.Reason);
        Assert.True(result.MemoryTerminal);
        Assert.Null(result.Error);
        Assert.Equal(1, result.RejectedCalls);
        Assert.Equal(5, result.Checks);
        Assert.Equal(0, result.MemoryChargedBytes);
        Assert.Equal(0, result.ParentChargedBytes);
    }
}
