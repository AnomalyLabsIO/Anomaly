using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Tests.EventFixtures.Valid;
using System.Reflection;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class MemoryFatalHostTests
{
    [Fact]
    public void Fatal_snapshot_getter_failure_invalidates_every_recipient_and_escapes_the_publisher()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        using var first = new ScriptSession(bindings, new ScriptExecutionOptions { Timeout = TimeSpan.FromSeconds(1) }, null, catalog);
        using var second = new ScriptSession(bindings, new ScriptExecutionOptions { Timeout = TimeSpan.FromSeconds(1) }, null, catalog);
        foreach (var session in new[] { first, second })
            Assert.True(session.Load("Events.Signals.Changed.Add(function() error('must never run') end)").Succeeded);
        SignalsEvents.Publish(new Payload()); // Also invalidate deliveries already in the queues.
        Payload.ThrowOnRead = true;
        try
        {
            var failure = Assert.Throws<OutOfMemoryException>(() => SignalsEvents.Publish(new Payload()));
            Assert.Equal("synthetic event getter failure", failure.Message);
        }
        finally { Payload.ThrowOnRead = false; }
        Assert.False(first.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.False(second.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.True(first.IsRetired);
        Assert.True(second.IsRetired);
        Assert.Equal(1, SignalsEvents.Removes);
    }

    [Theory]
    [InlineData("return pcall(function() return fixture:Fail() end)")]
    [InlineData("return pcall(function() return fixture.Value end)")]
    [InlineData("return pcall(function() return fixture:Wrapped() end)")]
    [InlineData("return pcall(function() return table.sort({3,2,1},function(a,b) return fixture:Fail() end) end)")]
    public void Fatal_host_allocation_failures_are_preserved_and_block_further_execution(string source)
    {
        using var runtime = new ScriptRuntime();
        runtime.Bindings.RegisterType<FatalFixture>();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions { Timeout = TimeSpan.FromSeconds(1) });
        session.SetGlobal("fixture", new FatalFixture());

        var failure = Assert.Throws<OutOfMemoryException>(() => session.Load(source));
        Assert.Equal("synthetic fixture failure; no memory exhaustion", failure.Message);
        Assert.Equal(ScriptExecutionStatus.Retired, session.Load("return 1").Status);
        Assert.True(session.IsRetired);
    }

    [ScriptType]
    public sealed class FatalFixture
    {
        [ScriptVisible] public int Value => throw SyntheticFailure();
        [ScriptVisible] public int Fail() => throw SyntheticFailure();
        [ScriptVisible] public int Wrapped() => throw new TargetInvocationException(
            new TypeInitializationException("synthetic fixture", SyntheticFailure()));
        private static OutOfMemoryException SyntheticFailure() => new("synthetic fixture failure; no memory exhaustion");
    }
}
