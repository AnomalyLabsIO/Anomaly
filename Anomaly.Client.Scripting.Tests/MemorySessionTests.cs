using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class MemorySessionTests
{
    [Fact]
    public void Session_entries_report_outstanding_and_cumulative_allocation_separately()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions
        {
            AllocationLimitBytes = 16L * 1024 * 1024,
            Timeout = TimeSpan.FromSeconds(1)
        });
        Assert.True(session.MemoryChargedBytes > 0);
        var initial = session.MemoryChargedBytes;
        Assert.True(session.Load("function allocate() local s=string.rep('x',4096); return #s end").Succeeded);
        Assert.True(session.MemoryChargedBytes > initial);
        for (var index = 0; index < 32; index++)
        {
            var before = session.MemoryTotalAllocatedBytes;
            var result = session.Invoke("allocate");
            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(4096d, result.Value);
            Assert.True(session.MemoryTotalAllocatedBytes > before);
            Assert.InRange(session.MemoryChargedBytes, 1, 16 * 1024 * 1024);
        }
        Assert.False(session.IsRetired);
        Assert.Equal(0, session.MemoryUnboundBytes);
    }

    [Fact]
    public void Host_global_conversion_is_charged_even_outside_a_lua_entry_point()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions { AllocationLimitBytes = 16L * 1024 * 1024 });
        var text = new string('x', 4096);
        for (var index = 0; index < 32; index++)
        {
            var before = session.MemoryTotalAllocatedBytes;
            session.SetGlobal("payload", text);
            Assert.True(session.MemoryTotalAllocatedBytes > before);
            Assert.InRange(session.MemoryChargedBytes, 1, 16 * 1024 * 1024);
        }
        Assert.False(session.IsRetired);
        Assert.Equal(0, session.MemoryUnboundBytes);
    }
}
