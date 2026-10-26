using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ClrAllocationExecutionTests
{
    [Theory]
    [InlineData("clralloc-error-format-preflight")]
    [InlineData("clralloc-error-alignment-preflight")]
    [InlineData("clralloc-exception-preflight")]
    [InlineData("clralloc-randomseed-preflight")]
    [InlineData("clralloc-location-preflight")]
    [InlineData("clralloc-tostring-preflight")]
    [InlineData("clralloc-tuple-print-preflight")]
    [InlineData("clralloc-list-capacity-preflight")]
    [InlineData("clralloc-dictionary-capacity-preflight")]
    [InlineData("clralloc-hashset-capacity-preflight")]
    public async Task Restricted_engine_allocations_abort_before_large_clr_storage_is_created(string fixture)
    {
        var result = await CpuProbeProcess.Run(fixture);
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("MemoryLimit", result.Reason);
        Assert.True(result.Terminal);
        Assert.True(result.MemoryTerminal);
        Assert.Null(result.Error);
        // This measures allocations on the worker thread during the operation;
        // it is neither a script heap measurement nor a process-memory bound.
        Assert.InRange(result.AllocationDeltaBytes, 0, 256 * 1024 - 1);
    }

    [Fact]
    public async Task Base_conversion_scans_whitespace_without_copying_the_input()
    {
        var result = await CpuProbeProcess.Run("clralloc-tonumber-no-trim-copy");
        Assert.Equal("Completed", result.Outcome);
        Assert.Equal(1d, result.Number);
        Assert.False(result.MemoryTerminal);
        Assert.Null(result.Error);
        Assert.InRange(result.AllocationDeltaBytes, 0, 256 * 1024 - 1);
    }

    [Fact]
    public async Task Discarded_ordinary_lua_errors_are_accounted_and_reclaimed()
    {
        var result = await CpuProbeProcess.Run("clralloc-error-loop-reclaim");
        Assert.Equal("Completed", result.Outcome);
        Assert.Equal(1024, result.Entries);
        Assert.False(result.MemoryTerminal);
        Assert.Null(result.Error);
        Assert.True(result.ReleasedRecords > 0);
        Assert.True(result.Checks >= 1026);
    }
}
