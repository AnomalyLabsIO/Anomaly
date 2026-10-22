using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Tests.EventFixtures.Valid;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class EventDispatchTests
{
    [Fact]
    public void SubscriptionsUsePascalCaseDotAndColonOperationsAndOnlyDispatchOnTheOwnerQueue()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        using var session = new ScriptSession(bindings, Options(), null, catalog);
        Assert.True(session.Load("seen = ''; local e=Events.Signals.Changed; " +
            "e.Add(function(a) seen = seen .. a.Text end); e:Add(function(a) seen=seen .. '!' end); " +
            "function Read() return seen end").Succeeded);
        SignalsEvents.Publish(new Payload { Text = "queued" });
        Assert.Equal("", session.Invoke("Read").Value);
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal("queued!", session.Invoke("Read").Value);
        Assert.Equal(1, SignalsEvents.Adds);
    }

    [Fact]
    public void RemovalThenReaddDoesNotDeliverOldQueuedGenerations()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        using var session = new ScriptSession(bindings, Options(), null, catalog);
        Assert.True(session.Load("seen=''; local e=Events.Signals.Changed; local cb=function(a) seen=a.Text end; " +
            "local token=e.Add(cb); function Remove() return e.Remove(cb) end; " +
            "function Add() token=e.Add(cb) end; function Read() return seen end").Succeeded);
        SignalsEvents.Publish(new Payload { Text = "old" });
        Assert.Equal(true, session.Invoke("Remove").Value);
        Assert.True(session.Invoke("Add").Succeeded);
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal("", session.Invoke("Read").Value);
        SignalsEvents.Publish(new Payload { Text = "new" });
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal("new", session.Invoke("Read").Value);
    }

    [Theory]
    [InlineData("Events.Signals.Changed.Add(print)")]
    [InlineData("Events.Signals.Changed = {}")]
    [InlineData("Events.Signals.Changed.Add(1)")]
    [InlineData("local e=Events.Signals.Changed; local f=e.Add; f(e,function()end)")]
    [InlineData("local fake={Add=Events.Signals.Changed.Add}; fake:Add(function()end)")]
    public void InvalidSubscriptionOperationsFailAndRetireTheSession(string source)
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        using var session = new ScriptSession(bindings, Options(), null, catalog);
        Assert.Equal(ScriptExecutionStatus.ScriptError, session.Load(source).Status);
        Assert.True(session.IsRetired);
        Assert.Equal(0, SignalsEvents.Adds);
    }

    [Fact]
    public void ParameterlessCallbacksDiscardResultsAndCleanupDetachesAllBridges()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        using var session = new ScriptSession(bindings, Options(), null, catalog);
        Assert.True(session.Load("count=0; Events.Signals.Empty.Add(function() count=count+1; return {} end); " +
            "Events.Signals.Changed.Add(function() end); function Read() return count end").Succeeded);
        SignalsEvents.PublishEmpty();
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal(1d, session.Invoke("Read").Value);
        SignalsEvents.Publish(new Payload());
        session.Dispose();
        Assert.False(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, SignalsEvents.Removes);
        session.Dispose();
        Assert.Equal(1, SignalsEvents.Removes);
    }

    [Fact]
    public void FiniteExpensiveEventCallbackRunsInsideTheCpuBudgetAndRetiresItsOwner()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        using var session = new ScriptSession(bindings, Options() with { InstructionLimit = 2_000 }, null, catalog);
        Assert.True(session.Load("Events.Signals.Changed.Add(function() local n=0; for i=1,100000 do n=n+i end end)").Succeeded);
        SignalsEvents.Publish(new Payload());
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.True(session.IsRetired);
        Assert.Equal(1, SignalsEvents.Removes);
    }

    private static ScriptExecutionOptions Options() => new() { Timeout = TimeSpan.FromSeconds(1) };
}
