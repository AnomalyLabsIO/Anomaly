using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class MemoryExecutionBudgetTests
{
    [Fact]
    public async Task Bounded_work_and_bootstrap_are_charged_without_exhausting_the_allowance()
    {
        var result = await CpuProbeProcess.Run("memory-bounded");
        Assert.Equal("Completed", result.Outcome);
        Assert.Equal(356d, result.Number);
        Assert.False(result.MemoryTerminal);
        Assert.InRange(result.MemoryChargedBytes, 1, result.MemoryLimitBytes);
    }

    [Theory]
    [InlineData("memory-script-initialization")]
    [InlineData("memory-large-rep")]
    [InlineData("memory-repeated-format")]
    [InlineData("memory-pcall-swallow")]
    [InlineData("memory-xpcall-swallow")]
    [InlineData("memory-growing-tables")]
    [InlineData("memory-growing-array")]
    [InlineData("memory-growing-prime-table")]
    [InlineData("memory-growing-concat")]
    [InlineData("memory-discarded-strings")]
    [InlineData("memory-growing-closures")]
    [InlineData("memory-table-unpack")]
    [InlineData("memory-table-concat")]
    [InlineData("memory-table-sort")]
    [InlineData("memory-sort-callback-abort")]
    [InlineData("memory-lua-coroutines")]
    [InlineData("memory-source-compilation")]
    [InlineData("memory-source-instructions")]
    public async Task Allocation_abuse_terminates_with_the_engine_memory_reason(string fixture)
    {
        AssertMemoryAbort(await CpuProbeProcess.Run(fixture));
    }

    [Fact]
    public async Task Independent_entries_cannot_reset_the_lifetime_allocation_allowance()
    {
        var result = await CpuProbeProcess.Run("memory-cumulative-entries");
        AssertMemoryAbort(result);
        Assert.InRange(result.Entries, 2, 999);
        Assert.Equal(4096d, result.Number);
    }

    [Theory]
    [InlineData("memory-preallocation-rep")]
    [InlineData("memory-preallocation-unpack")]
    public async Task Large_requests_abort_before_allocating_the_requested_string_or_array(string fixture)
    {
        var result = await CpuProbeProcess.Run(fixture);
        AssertMemoryAbort(result);
        // Actual allocations on this worker thread during the precompiled call,
        // including exception handling. This is neither heap size nor peak RSS.
        Assert.InRange(result.AllocationDeltaBytes, 0, 4L * 1024 * 1024 - 1);
    }

    [Fact]
    public async Task Coroutines_share_the_owning_scripts_allocation_allowance()
    {
        var result = await CpuProbeProcess.Run("memory-cumulative-coroutines");
        AssertMemoryAbort(result);
        Assert.InRange(result.Resumes, 1, 4095);
    }

    [Fact]
    public async Task Managed_callback_reservations_use_the_current_script_scope()
    {
        var result = await CpuProbeProcess.Run("memory-host-callback-scope");
        AssertMemoryAbort(result);
        Assert.Equal(1, result.HostCalls);
    }

    [Fact]
    public async Task A_clr_callback_cannot_convert_memory_abort_to_a_successful_return()
    {
        var result = await CpuProbeProcess.Run("memory-swallowed-host-abort");
        AssertMemoryAbort(result);
        Assert.Equal(1, result.HostCalls);
        Assert.Equal(1, result.RejectedCalls);
    }

    [Fact]
    public async Task A_memory_abort_stays_terminal_after_new_cpu_budgets_are_installed()
    {
        var result = await CpuProbeProcess.Run("memory-terminal-repeat-calls");
        AssertMemoryAbort(result);
        Assert.Equal(4, result.RejectedCalls);
    }

    private static void AssertMemoryAbort(CpuProbeResult result)
    {
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("MemoryLimit", result.Reason);
        Assert.True(result.Terminal);
        Assert.True(result.MemoryTerminal);
        Assert.Null(result.Error);
        Assert.InRange(result.MemoryChargedBytes, 0, result.MemoryLimitBytes);
    }
}
