using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ReclaimableExecutionBudgetTests
{
    [Fact]
    public async Task Ten_thousand_host_invocations_reclaim_coroutines_and_leave_no_unknown_allocation_debt()
    {
        var result = await CpuProbeProcess.Run("reclaimable-host-session");
        Assert.Equal("Completed", result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(10000, result.Entries);
        Assert.Equal(1024d, result.Number);
        Assert.Equal(20006, result.Checks);
        Assert.InRange(result.MemoryChargedBytes, 1, result.MemoryLimitBytes);
        Assert.True(result.TotalAllocatedBytes > result.MemoryLimitBytes);
        Assert.Equal(0, result.UnboundBytes);
    }

    [Fact]
    public async Task Repeated_value_assignment_keeps_current_storage_charged_and_releases_overwritten_storage()
    {
        var result = await CpuProbeProcess.Run("reclaimable-value-assignment");
        Assert.Equal("Completed", result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(10000, result.Entries);
        Assert.Equal(3, result.Checks);
        Assert.Equal(0, result.MemoryChargedBytes);
        Assert.Equal(0, result.UnboundBytes);
        Assert.True(result.TotalAllocatedBytes > result.MemoryLimitBytes);
    }

    [Fact]
    public async Task Ten_thousand_bounded_entries_reuse_collected_storage_in_one_script()
    {
        var result = await CpuProbeProcess.Run("reclaimable-bounded-entries");
        Assert.Equal("Completed", result.Outcome);
        Assert.Null(result.Error);
        Assert.False(result.MemoryTerminal);
        Assert.Equal(10000, result.Entries);
        Assert.Equal(1024d, result.Number);
        Assert.True(result.TotalAllocatedBytes > result.MemoryLimitBytes);
        Assert.InRange(result.MemoryChargedBytes, 1, result.MemoryLimitBytes);
        Assert.InRange(result.PeakBytes, result.MemoryChargedBytes, result.MemoryLimitBytes);
        Assert.True(result.ReleasedRecords > 0);
    }

    [Theory]
    [InlineData("reclaimable-retained-strings")]
    [InlineData("reclaimable-retained-tables")]
    [InlineData("reclaimable-retained-closures")]
    [InlineData("reclaimable-retained-prime-tables")]
    public async Task Retained_storage_exhausts_the_quota_despite_collections_and_sweeps(string fixture)
    {
        var result = await CpuProbeProcess.Run(fixture);
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("MemoryLimit", result.Reason);
        Assert.True(result.MemoryTerminal);
        Assert.Null(result.Error);
        Assert.InRange(result.Entries, 1, 4999);
        Assert.InRange(result.MemoryChargedBytes, 1, result.MemoryLimitBytes);
        Assert.InRange(result.PeakBytes, result.MemoryChargedBytes, result.MemoryLimitBytes);
    }
}
