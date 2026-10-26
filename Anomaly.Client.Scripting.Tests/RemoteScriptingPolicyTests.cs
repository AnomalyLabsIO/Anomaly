using Anomaly.Client.Api.Scripting;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class RemoteScriptingPolicyTests
{
    [Fact]
    public void UnqualifiedRemoteSessionRejectsBeforeSourceValidationOptionsOrVmAllocation()
    {
        using var runtime = new ScriptRuntime();
        long before = runtime.MemoryTotalAllocatedBytes;
        var invalidOptions = new ScriptExecutionOptions { InstructionLimit = -1 };
        var exception = Assert.Throws<NotSupportedException>(() => runtime.CreateApprovedSession(null!, invalidOptions));
        Assert.Equal(ScriptingPolicy.RemoteExecutionDisabledReason, exception.Message);
        Assert.False(ScriptingPolicy.MemoryHardeningQualified);
        Assert.False(ScriptingPolicy.RemoteExecutionEnabled);
        Assert.Equal(before, runtime.MemoryTotalAllocatedBytes);
        using var local = runtime.CreateDevelopmentSession();
        Assert.True(local.Load("return 1").Succeeded);
    }

    [Fact]
    public void ExactContentApprovalCannotBypassRemoteMemoryGate()
    {
        using var runtime = new ScriptRuntime();
        using var scripts = new ConnectionScriptSet(runtime, () => true);
        long before = runtime.MemoryTotalAllocatedBytes;
        Assert.False(scripts.Initialize(new ScriptSource[] { null! }, approved: true, out string error));
        Assert.Equal(ScriptingPolicy.RemoteExecutionDisabledReason, error);
        Assert.False(scripts.IsHealthy);
        Assert.Equal(before, runtime.MemoryTotalAllocatedBytes);
    }

    [Fact]
    public void AssetOnlyConnectionNeedsNoScriptMemoryQualification()
    {
        using var runtime = new ScriptRuntime();
        using var scripts = new ConnectionScriptSet(runtime, () => true);
        Assert.True(scripts.Initialize(Array.Empty<ScriptSource>(), approved: false, out string error), error);
        Assert.True(scripts.IsHealthy);
    }
}
