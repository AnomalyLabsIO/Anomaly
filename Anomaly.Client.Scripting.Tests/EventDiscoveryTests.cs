using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Events;
using Anomaly.Client.Scripting.Tests.EventFixtures.Valid;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class EventDiscoveryTests
{
    private const string ValidNamespace = "Anomaly.Client.Scripting.Tests.EventFixtures.Valid";

    [Fact]
    public void DiscoveryRequiresTheSuppliedAssemblyExactNamespaceAndExplicitMarker()
    {
        var bindings = new ScriptBindingRegistry();
        using var catalog = Discover(bindings);
        Assert.Equal(new[] { "Changed", "Empty", "Observed" }, catalog.GetCatalog().Select(value => value.Name));
        Assert.All(catalog.GetCatalog(), entry => Assert.Equal("Signals", entry.Category));
        Assert.True(catalog.GetCatalog().Single(entry => entry.Name == "Observed").CoalesceLatest);
        Assert.False(catalog.GetCatalog().Single(entry => entry.Name == "Changed").CoalesceLatest);
        Assert.Contains("type Payload", bindings.DescribeSurface());
        using var wrongAssembly = ScriptEventCatalog.Discover(new ScriptBindingRegistry(), typeof(object).Assembly, ValidNamespace);
        Assert.Empty(wrongAssembly.GetCatalog());
        using var wrongNamespace = ScriptEventCatalog.Discover(new ScriptBindingRegistry(), typeof(EventDiscoveryTests).Assembly, ValidNamespace + ".Nested");
        Assert.Empty(wrongNamespace.GetCatalog());
    }

    [Theory]
    [InlineData("BadSignature")]
    [InlineData("BadReturn")]
    [InlineData("BadArgument")]
    [InlineData("Writable")]
    [InlineData("Callable")]
    [InlineData("Keyword")]
    [InlineData("Duplicate")]
    public void UnsafeExplicitEventDefinitionsFailClosedBeforeSubscriptions(string scope)
    {
        SignalsEvents.Reset();
        Assert.ThrowsAny<Exception>(() => ScriptEventCatalog.Discover(new ScriptBindingRegistry(),
            typeof(EventDiscoveryTests).Assembly, "Anomaly.Client.Scripting.Tests.EventFixtures." + scope));
        Assert.Equal(0, SignalsEvents.Adds);
    }

    [Fact]
    public void FirstAndLastCallbacksShareOneClrBridgeAcrossSessions()
    {
        SignalsEvents.Reset();
        using var catalog = Discover(new ScriptBindingRegistry());
        var first = NewScript();
        var second = NewScript();
        var queue = new List<ScriptEventInvocation>();
        using var firstSubscriptions = new ScriptEventSubscriptions(catalog, first, queue.Add);
        using var secondSubscriptions = new ScriptEventSubscriptions(catalog, second, queue.Add);
        var source = catalog.Categories["Signals"]["Changed"];
        var one = firstSubscriptions.Add(source, Function(first));
        var two = secondSubscriptions.Add(source, Function(second));
        Assert.Equal(1, SignalsEvents.Adds);
        SignalsEvents.Publish(new Payload());
        Assert.Equal(2, queue.Count);
        Assert.True(firstSubscriptions.Remove(source, DynValue.NewString(one)));
        Assert.False(queue[0].IsActive);
        Assert.True(queue[1].IsActive);
        Assert.Equal(0, SignalsEvents.Removes);
        Assert.True(secondSubscriptions.Remove(source, DynValue.NewString(two)));
        Assert.Equal(1, SignalsEvents.Removes);
    }

    [Fact]
    public void SubscriptionCapsAndClosureRemovalAreSessionScopedAndTokensAreNotReused()
    {
        SignalsEvents.Reset();
        using var catalog = Discover(new ScriptBindingRegistry());
        var script = NewScript();
        var callback = Function(script);
        using var subscriptions = new ScriptEventSubscriptions(catalog, script, _ => { }, maxSubscriptions: 2);
        var source = catalog.Categories["Signals"]["Changed"];
        var first = subscriptions.Add(source, callback);
        var second = subscriptions.Add(source, callback);
        Assert.Throws<ScriptRuntimeException>(() => subscriptions.Add(source, callback));
        Assert.Equal(2, subscriptions.Count);
        Assert.True(subscriptions.Remove(source, callback.Clone()));
        Assert.Equal(0, subscriptions.Count);
        Assert.False(subscriptions.Remove(source, DynValue.NewString(first)));
        var next = subscriptions.Add(source, callback);
        Assert.NotEqual(first, next);
        Assert.NotEqual(second, next);
        using var other = new ScriptEventSubscriptions(catalog, NewScript(), _ => { });
        Assert.False(other.Remove(source, DynValue.NewString(next)));
    }

    [Fact]
    public void ForeignLuaAndClrCallbacksAreRejectedBeforeClrSubscription()
    {
        SignalsEvents.Reset();
        using var catalog = Discover(new ScriptBindingRegistry());
        var script = NewScript();
        using var subscriptions = new ScriptEventSubscriptions(catalog, script, _ => { });
        var source = catalog.Categories["Signals"]["Changed"];
        Assert.Throws<ScriptRuntimeException>(() => subscriptions.Add(source, Function(NewScript())));
        Assert.Throws<ScriptRuntimeException>(() => subscriptions.Add(source, DynValue.NewCallback((_, _) => DynValue.Nil)));
        Assert.Equal(0, SignalsEvents.Adds);
        Assert.Equal(0, subscriptions.Count);
    }

    [Fact]
    public void AddFailureRollsBackAndQuarantinesTheFaultyAccessor()
    {
        SignalsEvents.Reset();
        using var catalog = Discover(new ScriptBindingRegistry());
        var script = NewScript();
        using var subscriptions = new ScriptEventSubscriptions(catalog, script, _ => throw new Exception("must not publish"));
        var source = catalog.Categories["Signals"]["Changed"];
        SignalsEvents.ThrowOnAdd = true;
        Assert.Throws<ScriptRuntimeException>(() => subscriptions.Add(source, Function(script)));
        SignalsEvents.ThrowOnAdd = false;
        Assert.Throws<ScriptRuntimeException>(() => subscriptions.Add(source, Function(script)));
        Assert.Equal(0, subscriptions.Count);
        Assert.Equal(1, SignalsEvents.Adds);
        Assert.Equal(1, SignalsEvents.Removes);
        SignalsEvents.Publish(new Payload());
    }

    [Fact]
    public void DisposalDuringAddCannotReactivateOrLeakTheRegistration()
    {
        SignalsEvents.Reset();
        using var catalog = Discover(new ScriptBindingRegistry());
        var script = NewScript();
        using var subscriptions = new ScriptEventSubscriptions(catalog, script, _ => throw new Exception("must not publish"));
        SignalsEvents.DuringAdd = subscriptions.Dispose;
        Assert.Throws<ScriptRuntimeException>(() => subscriptions.Add(catalog.Categories["Signals"]["Changed"], Function(script)));
        Assert.Equal(0, subscriptions.Count);
        Assert.Equal(1, SignalsEvents.Removes);
        SignalsEvents.Publish(new Payload());
    }

    [Fact]
    public void RemoveFailureInvalidatesPendingWorkAndRequestsOwnerRetirement()
    {
        SignalsEvents.Reset();
        using var catalog = Discover(new ScriptBindingRegistry());
        var script = NewScript();
        var queue = new List<ScriptEventInvocation>();
        var faults = 0;
        using var subscriptions = new ScriptEventSubscriptions(catalog, script, queue.Add, requestRetirement: () => faults++);
        var source = catalog.Categories["Signals"]["Changed"];
        var token = subscriptions.Add(source, Function(script));
        SignalsEvents.Publish(new Payload());
        SignalsEvents.ThrowOnRemove = true;
        Assert.Throws<ScriptRuntimeException>(() => subscriptions.Remove(source, DynValue.NewString(token)));
        Assert.False(Assert.Single(queue).IsActive);
        SignalsEvents.Publish(new Payload());
        Assert.Single(queue);
        Assert.Equal(1, faults);
        SignalsEvents.ThrowOnRemove = false;
    }

    [Fact]
    public void OffThreadPublishRequestsRetirementWithoutReadingArgumentsOrEnteringVm()
    {
        SignalsEvents.Reset();
        using var catalog = Discover(new ScriptBindingRegistry());
        var script = NewScript();
        var queue = new List<ScriptEventInvocation>();
        var faults = 0;
        using var subscriptions = new ScriptEventSubscriptions(catalog, script, queue.Add,
            requestRetirement: () => Interlocked.Increment(ref faults));
        subscriptions.Add(catalog.Categories["Signals"]["Changed"], Function(script));
        Exception? failure = null;
        var publisher = new Thread(() => { try { SignalsEvents.Publish(new Payload()); } catch (Exception ex) { failure = ex; } });
        publisher.Start();
        Assert.True(publisher.Join(TimeSpan.FromSeconds(2)));
        Assert.Null(failure);
        Assert.Equal(1, faults);
        Assert.Equal(0, Payload.GetterCalls);
        Assert.Empty(queue);
        Assert.True(subscriptions.IsUnavailable);
    }

    [Fact]
    public void SnapshotFreezesSelectedPropertiesAndIsReadOnlyInLua()
    {
        SignalsEvents.Reset();
        using var catalog = Discover(new ScriptBindingRegistry());
        var script = NewScript();
        var queue = new List<ScriptEventInvocation>();
        using var subscriptions = new ScriptEventSubscriptions(catalog, script, queue.Add);
        subscriptions.Add(catalog.Categories["Signals"]["Changed"], Function(script));
        var payload = new Payload { Text = "before", Next = new Payload { Text = "child before" } };
        SignalsEvents.Publish(payload);
        payload.Text = "after";
        payload.Next.Text = "child after";
        var invocation = Assert.Single(queue);
        script.Globals.Set("captured", Assert.Single(invocation.ResolveArguments(script)));
        Assert.Equal("before:child before", script.DoString("return captured.Text .. ':' .. captured.Next.Text").String);
        Assert.True(script.DoString("return captured.GetType == nil and captured.Fields == nil").Boolean);
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("captured.Text = 'changed'"));
        Assert.Throws<ScriptRuntimeException>(() => invocation.ResolveArguments(NewScript()));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("unicode")]
    [InlineData("payload")]
    [InlineData("cycle")]
    [InlineData("depth")]
    [InlineData("nodes")]
    [InlineData("polymorphism")]
    public void SnapshotLimitFailuresRetireSubscribersWithoutEnqueueing(string kind)
    {
        SignalsEvents.Reset();
        using var catalog = Discover(new ScriptBindingRegistry());
        var script = NewScript();
        var queue = new List<ScriptEventInvocation>();
        var faults = 0;
        using var subscriptions = new ScriptEventSubscriptions(catalog, script, queue.Add, requestRetirement: () => faults++);
        subscriptions.Add(catalog.Categories["Signals"]["Changed"], Function(script));
        Payload payload = kind == "polymorphism" ? new DerivedPayload() : new Payload();
        if (kind == "string") payload.Text = new string('x', 4097);
        if (kind == "unicode") payload.Text = new string('\u00e9', 2049);
        if (kind == "payload") payload.Text = payload.Second = payload.Third = payload.Fourth = payload.Fifth = new string('x', 4000);
        if (kind == "cycle") payload.Next = payload;
        if (kind == "depth") { var tail = payload; for (var i = 0; i < 10; i++) { tail.Next = new Payload(); tail = tail.Next; } }
        if (kind == "nodes")
        {
            var tail = payload;
            for (var i = 0; i < 5; i++) { var next = new Payload(); tail.Branch = tail.Next = next; tail = next; }
        }
        SignalsEvents.Publish(payload);
        Assert.Empty(queue);
        Assert.Equal(1, faults);
        Assert.True(subscriptions.IsUnavailable);
    }

    internal static ScriptEventCatalog Discover(ScriptBindingRegistry bindings)
        => ScriptEventCatalog.Discover(bindings, typeof(EventDiscoveryTests).Assembly, ValidNamespace);
    internal static Script NewScript() => new(CoreModules.Basic | CoreModules.ErrorHandling | CoreModules.String);
    private static DynValue Function(Script script) => script.DoString("return function() end");
}
