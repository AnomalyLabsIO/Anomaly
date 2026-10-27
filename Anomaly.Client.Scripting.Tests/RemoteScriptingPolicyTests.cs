using System.Security.Cryptography;
using System.Text;
using Anomaly.Client.Api.Scripting;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class RemoteScriptingPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsolatedWorkerQualificationNeverQualifiesTheInProcessAllocationAllowance(bool qualified)
    {
        using var policy = new Qualification(qualified, "The actual worker boundary probe failed.");
        Assert.False(ScriptingPolicy.MemoryHardeningQualified);
        Assert.Equal(qualified, ScriptingPolicy.RemoteIsolationQualified);
        Assert.Equal(qualified, ScriptingPolicy.RemoteExecutionEnabled);
        Assert.Equal(qualified ? string.Empty : "The actual worker boundary probe failed.",
            ScriptingPolicy.RemoteExecutionDisabledReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawRemoteSourceAlwaysRejectsBeforeOptionsOrInProcessVmAllocation(bool qualified)
    {
        using var policy = new Qualification(qualified, "Worker isolation is unavailable.");
        using var runtime = new ScriptRuntime();
        long before = runtime.MemoryTotalAllocatedBytes;
        var invalidOptions = new ScriptExecutionOptions { InstructionLimit = -1 };
        var exception = Assert.Throws<NotSupportedException>(() => runtime.CreateRemoteSession(null, invalidOptions));
        Assert.Contains("approved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, runtime.MemoryTotalAllocatedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifiedLocalHostSourcesRemainSynchronousAndIndependentOfWorkerQualification(bool qualified)
    {
        using var policy = new Qualification(qualified, "Worker isolation is unavailable.");
        using var runtime = new ScriptRuntime();
        using var scripts = new ConnectionScriptSet(runtime, () => true);
        const string text = "function Read() return 1 end";
        var source = new ScriptSource("fixture:local", "mods/fixture/start.lua", text,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(), text.Length);
        Assert.True(scripts.InitializeLocal(new[] { source }, approved: true, out var error), error);
        Assert.True(scripts.IsHealthy);
        Assert.False(ScriptingPolicy.MemoryHardeningQualified);
    }

    private sealed class Qualification : IDisposable
    {
        private readonly bool _qualified = ScriptingPolicy.RemoteIsolationQualified;
        private readonly string _reason = ScriptingPolicy.RemoteExecutionDisabledReason;

        internal Qualification(bool qualified, string reason)
        {
            typeof(ScriptingPolicy).GetProperty(nameof(ScriptingPolicy.RemoteIsolationQualified))!.SetValue(null, qualified);
            typeof(ScriptingPolicy).GetProperty(nameof(ScriptingPolicy.RemoteExecutionDisabledReason))!.SetValue(null, reason);
        }

        public void Dispose()
        {
            typeof(ScriptingPolicy).GetProperty(nameof(ScriptingPolicy.RemoteIsolationQualified))!.SetValue(null, _qualified);
            typeof(ScriptingPolicy).GetProperty(nameof(ScriptingPolicy.RemoteExecutionDisabledReason))!.SetValue(null, _reason);
        }
    }
}
