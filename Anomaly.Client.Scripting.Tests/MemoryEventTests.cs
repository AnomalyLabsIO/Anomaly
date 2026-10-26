using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Tests.EventFixtures.Valid;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class MemoryEventTests
{
    [Fact]
    public void Exhaustion_in_a_callback_detaches_subscriptions_and_drops_queued_work()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        var diagnostics = new List<string>();
        using var session = new ScriptSession(bindings, Options(), diagnostics.Add, catalog);
        Assert.True(session.Load("Events.Signals.Changed.Add(function() " +
            "pcall(function() return string.rep('x',8388608) end) end)").Succeeded);
        SignalsEvents.Publish(new Payload());
        SignalsEvents.Publish(new Payload());

        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.True(session.IsRetired);
        Assert.Equal(1, SignalsEvents.Removes);
        Assert.False(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Contains(diagnostics, message => message.Contains("MemoryLimit", StringComparison.Ordinal));
    }

    [Fact]
    public void Retained_snapshots_exhaust_the_recipient_quota_before_further_capture()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        using var session = new ScriptSession(bindings, Options(), null, catalog);
        Assert.True(session.Load("retained = {}; Events.Signals.Changed.Add(function(args) " +
            "retained[#retained+1] = args end)").Succeeded);
        var first = session.MemoryChargedBytes;
        var publications = 0;
        for (; publications < 64 && !session.IsRetired; publications++)
        {
            SignalsEvents.Publish(new Payload());
            session.DispatchQueuedEvent(TimeSpan.FromSeconds(1));
        }
        Assert.InRange(publications, 2, 63);
        Assert.True(session.IsRetired);
        Assert.True(session.MemoryChargedBytes > first);
        Assert.Equal(1, SignalsEvents.Removes);
        var getters = Payload.GetterCalls;
        SignalsEvents.Publish(new Payload());
        Assert.Equal(getters, Payload.GetterCalls);
    }

    [Fact]
    public void Repeated_disposal_after_memory_failure_does_not_rerun_resource_cleanup()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(Options());
        var resource = new Counter();
        session.TrackResource(resource);
        Assert.Equal(ScriptExecutionStatus.MemoryLimit,
            session.Load("return string.rep('x',8388608)").Status);
        session.Dispose();
        runtime.RevokeAll();
        Assert.Equal(1, resource.Disposals);
    }

    private static ScriptExecutionOptions Options() => new()
    {
        AllocationLimitBytes = 1024 * 1024,
        InstructionLimit = 10_000_000,
        Timeout = TimeSpan.FromSeconds(1)
    };

    private sealed class Counter : IDisposable
    {
        internal int Disposals;
        public void Dispose() => Disposals++;
    }
}
