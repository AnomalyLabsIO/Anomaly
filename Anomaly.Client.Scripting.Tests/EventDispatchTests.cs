using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Events;
using Anomaly.Client.Scripting.Tests.EventFixtures.Valid;
using MoonSharp.Interpreter;
using System.Runtime.CompilerServices;
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

    [Fact]
    public void ExplicitLatestObservationsKeepOnePendingSnapshotAndPreserveTheLatestValue()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        using var session = new ScriptSession(bindings, Options(), null, catalog);
        Assert.True(session.Load("seen=''; calls=0; Events.Signals.Observed.Add(function(a) seen=a.Text; calls=calls+1 end); " +
            "function Read() return seen end; function Calls() return calls end").Succeeded);
        for (var index = 0; index < 100; index++)
        {
            SignalsEvents.PublishObservation(new Payload { Text = index.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            Assert.Equal(1, session.QueuedCount);
        }
        Assert.False(session.IsRetired);
        Assert.Equal("", session.Invoke("Read").Value);
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal("99", session.Invoke("Read").Value);
        Assert.Equal(1d, session.Invoke("Calls").Value);
        SignalsEvents.PublishObservation(new Payload { Text = "next" });
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal("next", session.Invoke("Read").Value);
        Assert.Equal(2d, session.Invoke("Calls").Value);
    }

    [Fact]
    public void LatestObservationKeysAreRegistrationGenerationsNotCallbackIdentity()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        using var session = new ScriptSession(bindings, Options(), null, catalog);
        Assert.True(session.Load("seen=''; local e=Events.Signals.Observed; local cb=function(a) seen=seen..a.Text end; " +
            "local first=e.Add(cb); e.Add(cb); function Replace() e.Remove(first); first=e.Add(cb) end; " +
            "function Read() return seen end").Succeeded);
        SignalsEvents.PublishObservation(new Payload { Text = "old" });
        Assert.True(session.Invoke("Replace").Succeeded);
        SignalsEvents.PublishObservation(new Payload { Text = "latest" });
        Assert.Equal(3, session.QueuedCount);
        for (var index = 0; index < 3; index++) Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal("latestlatest", session.Invoke("Read").Value);
        Assert.False(session.IsRetired);
    }

    [Fact]
    public void ReliableDiscreteEventsPreserveEveryArrivalAndFailClosedOnRealOverflow()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        var diagnostics = new List<string>();
        using var session = new ScriptSession(bindings, Options(), diagnostics.Add, catalog);
        session.SetSourceIdentity("fixture:panel mods/fixture/start.lua");
        Assert.True(session.Load("seen=''; Events.Signals.Changed.Add(function(a) seen=seen..a.Text end); " +
            "function Read() return seen end").Succeeded);
        foreach (var text in new[] { "one", "two", "three" }) SignalsEvents.Publish(new Payload { Text = text });
        Assert.Equal(3, session.QueuedCount);
        for (var index = 0; index < 3; index++) Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal("onetwothree", session.Invoke("Read").Value);
        for (var index = 0; index < 65; index++) SignalsEvents.Publish(new Payload());
        Assert.True(session.IsRetired);
        Assert.Equal(0, session.QueuedCount);
        Assert.Contains(diagnostics, message => message.Contains("fixture:panel mods/fixture/start.lua", StringComparison.Ordinal) &&
            message.Contains("Events.Signals.Changed", StringComparison.Ordinal) && message.Contains("queue exceeded", StringComparison.Ordinal));
    }

    [Fact]
    public void SnapshotAndCpuFailuresIdentifyTheirSourceAndEvent()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        var diagnostics = new List<string>();
        using (var session = new ScriptSession(bindings, Options(), diagnostics.Add, catalog))
        {
            Assert.True(session.Load("Events.Signals.Observed.Add(function() end)", "mods/fixture/observation.lua").Succeeded);
            SignalsEvents.PublishObservation(new Payload { Text = new string('x', 4097) });
            Assert.False(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
            Assert.True(session.IsRetired);
            Assert.Contains(diagnostics, message => message.Contains("mods/fixture/observation.lua", StringComparison.Ordinal) &&
                message.Contains("Events.Signals.Observed", StringComparison.Ordinal) && message.Contains("strings exceed", StringComparison.Ordinal));
        }
        diagnostics.Clear();
        using var cpu = new ScriptSession(bindings, Options() with { InstructionLimit = 2_000 }, diagnostics.Add, catalog);
        Assert.True(cpu.Load("Events.Signals.Empty.Add(function() local n=0; for i=1,100000 do n=n+i end end)",
            "mods/fixture/work.lua").Succeeded);
        SignalsEvents.PublishEmpty();
        Assert.True(cpu.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.True(cpu.IsRetired);
        Assert.Contains(diagnostics, message => message.Contains("mods/fixture/work.lua", StringComparison.Ordinal) &&
            message.Contains("Events.Signals.Empty", StringComparison.Ordinal) && message.Contains("CpuLimit", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplacingLatestSnapshotsReleasesOldRootsWithoutRefundingRetainedBackingStorage()
    {
        SignalsEvents.Reset();
        using var catalog = EventDiscoveryTests.Discover(new ScriptBindingRegistry());
        var owner = EventDiscoveryTests.NewScript();
        var callback = owner.DoString("return function() end");
        owner.MemoryBudget = new MemoryBudget(4 * 1024 * 1024, reclaimable: true);
        var queue = new List<ScriptEventInvocation>();
        using var subscriptions = new ScriptEventSubscriptions(catalog, owner, queue.Add);
        subscriptions.Add(catalog.Categories["Signals"]["Observed"], callback);
        var previous = ReplaceObservationAndCheckLiveCharges(queue, owner.MemoryBudget);
        Assert.Single(queue);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(previous.IsAlive);
        owner.MemoryBudget.Sweep();
        Assert.True(owner.MemoryBudget.ChargedBytes >= 64 * 1024);
        Assert.Equal("latest", ((ScriptFrozenEventValue)queue[0].Arguments[0]!).Fields["Text"]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReplaceObservationAndCheckLiveCharges(List<ScriptEventInvocation> queue, MemoryBudget memory)
    {
        SignalsEvents.PublishObservation(new Payload { Text = "old" });
        var previous = queue[0].Arguments[0]!;
        var charged = memory.ChargedBytes;
        SignalsEvents.PublishObservation(new Payload { Text = "latest" });
        memory.Sweep();
        Assert.True(memory.ChargedBytes >= charged + 64 * 1024);
        GC.KeepAlive(previous);
        return new WeakReference(previous);
    }

    private static ScriptExecutionOptions Options() => new() { Timeout = TimeSpan.FromSeconds(1) };
}
