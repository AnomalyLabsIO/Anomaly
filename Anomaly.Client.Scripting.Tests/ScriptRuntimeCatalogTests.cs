using System.Reflection;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Scripting;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ScriptRuntimeCatalogTests
{
    [Fact]
    public void DiscoveryExportsAnnotatedTypesAndEventsWithoutNativeSubscriptions()
    {
        var before = ReadySubscribers();
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        runtime.DiscoverApi();
        var catalog = runtime.GetCatalog();
        Assert.Contains(catalog.Types, type => type.Name == "Player");
        Assert.Contains(catalog.Types, type => type.Name == "ClientReadyEventArgs");
        Assert.Contains(catalog.Types, type => type.Name == "ClientConnectedEventArgs");
        var events = typeof(ClientEvents).Assembly.GetTypes().Where(type => type.IsVisible && type.IsAbstract && type.IsSealed)
            .SelectMany(type => type.GetEvents(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(value => value.IsDefined(typeof(ScriptEventAttribute), false) && !value.IsDefined(typeof(ScriptIgnoreAttribute), false))
            .Select(value => (FullName: value.DeclaringType!.FullName!, value.Name)).OrderBy(value => value.FullName).ThenBy(value => value.Name).ToArray();
        var registered = catalog.Events.Select(value => (FullName: value.ClrDeclaringType, Name: value.ClrEventName))
            .OrderBy(value => value.FullName).ThenBy(value => value.Name).ToArray();
        Assert.Equal(events, registered);
        Assert.Equal(before, ReadySubscribers());
        Assert.False(ScriptingPolicy.RemoteExecutionEnabled);
        Assert.Throws<NotSupportedException>(() => runtime.CreateRemoteSession("return 1"));
    }

    [Fact]
    public void DiscoveryCannotReplaceAnActiveSessionsPolicy()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession();
        Assert.Throws<InvalidOperationException>(runtime.DiscoverApi);
        Assert.False(session.IsRetired);
    }

    [Fact]
    public void RuntimeSessionsShareLazyNativeBridgeAndQueueCallbacks()
    {
        var before = ReadySubscribers();
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        using var first = runtime.CreateDevelopmentSession();
        using var second = runtime.CreateDevelopmentSession();
        const string source = "calls=0; function callback(args) calls=calls+1; scene=args.SceneName end; " +
            "token=Events.Client.Ready.Add(callback); function count() return calls end; function read() return scene end";
        Assert.True(first.Load(source).Succeeded);
        Assert.True(second.Load(source).Succeeded);
        Assert.Equal(before + 1, ReadySubscribers());
        PublishReady("Facility");
        Assert.Equal(0d, first.Invoke("count").Value);
        Assert.Equal(0d, second.Invoke("count").Value);
        Assert.True(first.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.True(second.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal("Facility", first.Invoke("read").Value);
        Assert.Equal(1d, second.Invoke("count").Value);
        first.Dispose();
        Assert.Equal(before + 1, ReadySubscribers());
        second.Dispose();
        Assert.Equal(before, ReadySubscribers());
    }

    [Fact]
    public void RemovalInvalidatesOldQueueGenerationEvenWhenSameClosureIsAddedAgain()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        using var session = runtime.CreateDevelopmentSession();
        Assert.True(session.Load("calls=0; function callback(args) calls=calls+1 end; " +
            "Events.Client.Ready.Add(callback); function replace() " +
            "Events.Client.Ready.Remove(callback); Events.Client.Ready.Add(callback) end; function count() return calls end").Succeeded);
        PublishReady("first");
        Assert.True(session.Invoke("replace").Succeeded);
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal(0d, session.Invoke("count").Value);
        PublishReady("second");
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal(1d, session.Invoke("count").Value);
    }

    [Fact]
    public void LoadFailureRollsBackSubscriptionsAndDisconnectRevocationDropsQueuedWork()
    {
        var before = ReadySubscribers();
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        using var failed = runtime.CreateDevelopmentSession();
        Assert.Equal(ScriptExecutionStatus.ScriptError,
            failed.Load("Events.Client.Ready.Add(function(args) end); error('failed load')").Status);
        Assert.Equal(before, ReadySubscribers());
        using var active = runtime.CreateDevelopmentSession();
        Assert.True(active.Load("Events.Client.Ready.Add(function(args) error('must not run') end)").Succeeded);
        PublishReady("queued");
        runtime.RevokeAll();
        Assert.True(active.IsRetired);
        Assert.False(active.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal(before, ReadySubscribers());
    }

    [Fact]
    public void CallbackRunsThroughCpuBoundaryAndRetiresSubscriptions()
    {
        var before = ReadySubscribers();
        var diagnostics = new List<string>();
        using var runtime = new ScriptRuntime(diagnostics.Add);
        runtime.DiscoverApi();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions { InstructionLimit = 2_000 });
        Assert.True(session.Load("Events.Client.Ready.Add(function(args) local a=0; for i=1,100000 do a=a+i end end)").Succeeded);
        PublishReady("queued");
        Assert.False(session.IsRetired);
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.True(session.IsRetired);
        Assert.Contains(diagnostics, value => value.Contains("CpuLimit", StringComparison.Ordinal));
        Assert.Equal(before, ReadySubscribers());
    }

    [Fact]
    public void ForeignThreadPublisherRequestsOwnerRetirementWithoutExecutingLua()
    {
        var before = ReadySubscribers();
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        using var session = runtime.CreateDevelopmentSession();
        Assert.True(session.Load("calls=0; Events.Client.Ready.Add(function(args) calls=calls+1 end); function count() return calls end").Succeeded);
        Exception? failure = null;
        var publisher = new Thread(() => { try { PublishReady("foreign"); } catch (Exception exception) { failure = exception; } });
        publisher.Start();
        Assert.True(publisher.Join(TimeSpan.FromSeconds(2)));
        Assert.Null(failure);
        Assert.Equal(ScriptExecutionStatus.Retired, session.Invoke("count").Status);
        Assert.Equal(before, ReadySubscribers());
    }

    [Fact]
    public void CallbackReturnValuesAreDiscardedAndCannotLeakObjectsToHost()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        using var session = runtime.CreateDevelopmentSession();
        Assert.True(session.Load("Events.Client.Ready.Add(function(args) return {value=args.SceneName} end)").Succeeded);
        PublishReady("value");
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.False(session.IsRetired);
    }

    private static void PublishReady(string scene) => typeof(ClientEvents)
        .GetMethod("OnReady", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, new object[] { new ClientReadyEventArgs(scene) });

    private static int ReadySubscribers() => ((Delegate?)typeof(ClientEvents)
        .GetField("Ready", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null))?.GetInvocationList().Length ?? 0;
}
