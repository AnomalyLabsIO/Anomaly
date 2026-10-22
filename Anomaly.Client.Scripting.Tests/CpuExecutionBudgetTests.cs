using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class CpuExecutionBudgetTests
{
    [Fact]
    public async Task UsefulBoundedMainCallCompletes()
    {
        var result = await CpuProbeProcess.Run("bounded-main");
        Assert.Equal("Completed", result.Outcome);
        Assert.Equal(5050, result.Number);
        Assert.False(result.Terminal);
    }

    [Theory]
    [InlineData("main-loop")]
    [InlineData("pcall-loop")]
    [InlineData("xpcall-loop")]
    [InlineData("xpcall-error-handler-loop")]
    [InlineData("gsub-callback-loop")]
    [InlineData("sort-callback-loop")]
    [InlineData("clr-callback-reentry-loop")]
    [InlineData("coroutine-loop")]
    [InlineData("builtin-pattern-work")]
    public async Task RunawayExecutionAbortsInsideInterpreter(string fixture)
    {
        var result = await CpuProbeProcess.Run(fixture);
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("CpuLimit", result.Reason);
        Assert.True(result.Terminal);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("cumulative-resume-fuel")]
    [InlineData("cumulative-coroutine-fuel")]
    public async Task AllResumesAndCoroutinesShareFiniteFuel(string fixture)
    {
        var result = await CpuProbeProcess.Run(fixture);
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("CpuLimit", result.Reason);
        Assert.InRange(result.Resumes, 1, 20);
        Assert.True(result.Terminal);
    }

    [Theory]
    [InlineData("depth-limit")]
    [InlineData("source-nesting-depth")]
    [InlineData("cyclic-call-metamethod")]
    [InlineData("nested-clr-pcall-depth")]
    public async Task RecursionHitsEngineDepthBoundary(string fixture)
    {
        var result = await CpuProbeProcess.Run(fixture);
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("DepthLimit", result.Reason);
        Assert.True(result.Terminal);
    }

    [Fact]
    public async Task ExpiredDeadlineRejectsFurtherExecution()
    {
        var result = await CpuProbeProcess.Run("expired-deadline");
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("Deadline", result.Reason);
        Assert.True(result.Terminal);
    }

    [Theory]
    [InlineData("cancelled-lua-call")]
    [InlineData("cancelled-clr-call")]
    public async Task CancelledBudgetStopsLuaAndDirectClrEntryPoints(string fixture)
    {
        var result = await CpuProbeProcess.Run(fixture);
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("Cancelled", result.Reason);
        Assert.Equal(0, result.HostCalls);
        Assert.True(result.Terminal);
    }

    [Fact]
    public async Task PcallCannotSwallowTerminalHostCancellation()
    {
        var result = await CpuProbeProcess.Run("swallowed-host-abort");
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("Cancelled", result.Reason);
        Assert.Equal(1, result.HostCalls);
        Assert.True(result.Terminal);
    }

    [Fact]
    public async Task ExhaustedScriptRejectsEveryLaterCall()
    {
        var result = await CpuProbeProcess.Run("terminal-repeat-calls");
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("CpuLimit", result.Reason);
        Assert.Equal(4, result.RejectedCalls);
        Assert.True(result.Terminal);
    }

    [Fact]
    public async Task DeadlineIsEnforcedAfterFiniteClrWorkReturns()
    {
        var result = await CpuProbeProcess.Run("deadline-after-clr-return");
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("Deadline", result.Reason);
        Assert.Equal(1, result.HostCalls);
        Assert.True(result.Terminal);
    }

    [Fact]
    public async Task ClrWrapperCannotConvertTerminalBudgetToSuccessfulResult()
    {
        var result = await CpuProbeProcess.Run("terminal-clr-wrapper-catch");
        Assert.Equal("Aborted", result.Outcome);
        Assert.Equal("CpuLimit", result.Reason);
        Assert.Equal(1, result.HostCalls);
        Assert.True(result.Terminal);
    }
}
