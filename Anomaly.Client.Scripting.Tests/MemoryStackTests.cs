using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class MemoryStackTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(61)]
    [InlineData(62)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(124)]
    [InlineData(125)]
    [InlineData(126)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(256)]
    public void Growing_stacks_preserve_varargs_and_unwind_near_capacity_boundaries(int count)
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions { Timeout = TimeSpan.FromSeconds(1) });
        var result = session.Load("local function Count(...) return select('#',...) end; local values={}; " +
            "for i=1," + count + " do values[i]=i end; " +
            "local result=Count(table.unpack(values)); return result");
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal((double)count, result.Value);
        Assert.False(session.IsRetired);
    }
}
