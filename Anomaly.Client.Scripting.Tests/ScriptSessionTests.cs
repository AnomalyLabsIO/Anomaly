using Anomaly.Client.Api.Scripting;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ScriptSessionTests
{
    [Fact]
    public void Unverified_remote_source_is_rejected_before_source_or_option_processing()
    {
        using var runtime = new ScriptRuntime();
        Assert.False(ScriptingPolicy.MemoryHardeningQualified);
        Assert.False(ScriptingPolicy.RemoteExecutionEnabled);
        var invalidOptions = new ScriptExecutionOptions { InstructionLimit = -1 };
        var exception = Assert.Throws<NotSupportedException>(() => runtime.CreateRemoteSession(null, invalidOptions));
        Assert.Contains("approved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<NotSupportedException>(() => runtime.CreateRemoteSession("return 1"));
    }

    [Fact]
    public void ScriptsHaveNoFilesystemNetworkReflectionOrDynamicLoadGlobals()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession();
        var result = session.Load("return io == nil and os == nil and package == nil and require == nil " +
            "and load == nil and loadfile == nil and dofile == nil and loadstring == nil " +
            "and debug == nil and clr == nil and luanet == nil and socket == nil " +
            "and collectgarbage == nil and coroutine == nil and string.dump == nil");
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(true, result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\u001bLua")]
    [InlineData("return '\0'")]
    [InlineData("MoonSharp_dump_b64::attack")]
    public void NonSourcePackagesAreRejectedAndRetired(string? source)
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession();
        Assert.Equal(ScriptExecutionStatus.SourceRejected, session.Load(source).Status);
        Assert.True(session.IsRetired);
        Assert.Equal(ScriptExecutionStatus.Retired, session.Load("return 1").Status);
    }

    [Fact]
    public void SourceUsesUtf8ByteLimitBeforeParsing()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions { MaxSourceBytes = 32 });
        Assert.Equal(ScriptExecutionStatus.SourceRejected, session.Load("--" + new string('\u00e9', 16)).Status);
    }

    [Fact]
    public void CSharpCanInvokeFunctionsWithFreshBudgetAndBoundedValues()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions { Timeout = TimeSpan.FromMilliseconds(100) });
        Assert.True(session.Load("function add(a,b) return a+b end; function read() return title end").Succeeded);
        Thread.Sleep(120); // A completed invocation's deadline must not poison later host conversion.
        session.SetGlobal("title", "example");
        Assert.Equal("example", session.Invoke("read").Value);
        Assert.Equal(5d, session.Invoke("add", 2, 3).Value);
        Assert.Equal(9d, session.Invoke("add", 4, 5).Value);
    }

    [Theory]
    [InlineData("return {}")]
    [InlineData("return function() end")]
    [InlineData("return 0/0")]
    [InlineData("return string.rep('a', 33)")]
    public void ResultsCannotLeakGraphsFunctionsOrUnboundedStrings(string source)
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions { MaxResultStringBytes = 32 });
        Assert.Equal(ScriptExecutionStatus.InvalidResult, session.Load(source).Status);
        Assert.True(session.IsRetired);
    }

    [Fact]
    public void ScriptFailureRetiresVmAndCleansAllResourcesDespiteObserverFailures()
    {
        var cleanup = new List<int>();
        using var runtime = new ScriptRuntime(_ => throw new InvalidOperationException("logger"));
        using var session = runtime.CreateDevelopmentSession();
        session.TrackResource(new Cleanup(() => cleanup.Add(1)));
        session.TrackResource(new Cleanup(() => { cleanup.Add(2); throw new InvalidOperationException("resource"); }));
        Assert.Equal(ScriptExecutionStatus.ScriptError, session.Load("error('failed load')").Status);
        Assert.Equal(new[] { 2, 1 }, cleanup);
        Assert.Equal(ScriptExecutionStatus.Retired, session.Invoke("later").Status);
        session.Dispose();
        Assert.Equal(2, cleanup.Count);
    }

    [Fact]
    public void CpuFailureRetiresVmAndCannotBeResumedByHost()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession(new ScriptExecutionOptions { InstructionLimit = 2_000 });
        Assert.True(session.Load("function work() local a=0; for i=1,100000 do a=a+i end return a end").Succeeded);
        Assert.Equal(ScriptExecutionStatus.CpuLimit, session.Invoke("work").Status);
        Assert.True(session.IsRetired);
        Assert.Equal(ScriptExecutionStatus.Retired, session.Invoke("work").Status);
    }

    [Fact]
    public void CSharpEventsAreQueuedRatherThanCallingLuaSynchronously()
    {
        using var runtime = new ScriptRuntime(timestamp: () => 0);
        using var session = runtime.CreateDevelopmentSession();
        Assert.True(session.Load("seen=0; function on_ready(value) seen=seen+value end; function Read() return seen end").Succeeded);
        runtime.QueueEvent("on_ready", 3);
        Assert.Equal(0d, session.Invoke("Read").Value);
        runtime.Tick();
        Assert.False(session.IsRetired);
        Assert.Equal(3d, session.Invoke("Read").Value);
        Assert.Equal(0, session.QueuedCount);
    }

    [Fact]
    public void TwoArrivalsPerTickRemainHealthyBeyondTheFormerFrame64Overflow()
    {
        using var runtime = new ScriptRuntime(timestamp: () => 0);
        using var session = runtime.CreateDevelopmentSession();
        Assert.True(session.Load("seen=0; function Observe() seen=seen+1 end; function Read() return seen end").Succeeded);
        // Warm ordinary callback machinery independently of the scheduling clock.
        Assert.True(session.Invoke("Observe").Succeeded);
        for (var frame = 0; frame < 128; frame++)
        {
            Assert.True(session.QueueEvent("Observe"));
            Assert.True(session.QueueEvent("Observe"));
            runtime.Tick();
            Assert.False(session.IsRetired);
            Assert.Equal(0, session.QueuedCount);
            Assert.Equal(1d + 2d * (frame + 1), session.Invoke("Read").Value);
        }
    }

    [Fact]
    public void RuntimeRepeatsFairRoundsButStartsAtMostFourDeliveriesPerTick()
    {
        using var runtime = new ScriptRuntime(timestamp: () => 0);
        var sessions = Enumerable.Range(0, 8).Select(_ => runtime.CreateDevelopmentSession()).ToArray();
        foreach (var session in sessions)
        {
            Assert.True(session.Load("seen=0; function Observe() seen=seen+1 end; function Read() return seen end").Succeeded);
            Assert.True(session.Invoke("Observe").Succeeded);
            Assert.True(session.QueueEvent("Observe"));
            Assert.True(session.QueueEvent("Observe"));
        }
        for (var tick = 0; tick < 4; tick++)
        {
            runtime.Tick();
            Assert.Equal((tick + 1) * 4d, sessions.Sum(session => (double)session.Invoke("Read").Value! - 1d));
            var counts = sessions.Select(session => (double)session.Invoke("Read").Value!).ToArray();
            Assert.InRange(counts.Max() - counts.Min(), 0d, 1d);
            Assert.All(sessions, session => Assert.False(session.IsRetired));
        }
        Assert.All(sessions, session => Assert.Equal(0, session.QueuedCount));
    }

    [Fact]
    public void ACallbackAdmittedAtTheSchedulingBoundaryGetsItsOwnWallAllowance()
    {
        var window = Math.Max(1, System.Diagnostics.Stopwatch.Frequency / 500);
        var stamps = new[] { 0L, 0L, window - 1, window };
        var stamp = 0;
        using var runtime = new ScriptRuntime(timestamp: () => stamps[Math.Min(stamp++, stamps.Length - 1)]);
        using var session = runtime.CreateDevelopmentSession();
        Assert.True(session.Load("seen=0; function Observe() seen=seen+1 end; function Read() return seen end").Succeeded);
        Assert.True(session.Invoke("Observe").Succeeded);
        for (var index = 0; index < 3; index++) Assert.True(session.QueueEvent("Observe"));
        runtime.Tick();
        Assert.False(session.IsRetired);
        Assert.Equal(3d, session.Invoke("Read").Value);
        Assert.Equal(1, session.QueuedCount);
    }

    [Fact]
    public void DispatchRevocationStopsRepeatedRoundsBeforeAnotherCallback()
    {
        using var runtime = new ScriptRuntime(timestamp: () => 0);
        runtime.Bindings.RegisterType<Revoker>();
        using var first = runtime.CreateDevelopmentSession();
        using var second = runtime.CreateDevelopmentSession();
        var revoker = new Revoker(runtime.RevokeAll);
        first.SetGlobal("revoker", revoker);
        Assert.True(first.Load("function Observe() revoker:Revoke() end").Succeeded);
        Assert.True(second.Load("function Observe() error('must not execute') end").Succeeded);
        Assert.True(first.QueueEvent("Observe"));
        Assert.True(second.QueueEvent("Observe"));
        runtime.Tick();
        Assert.Equal(1, revoker.Calls);
        Assert.True(first.IsRetired);
        Assert.True(second.IsRetired);
        Assert.Equal(0, first.QueuedCount);
        Assert.Equal(0, second.QueuedCount);
    }

    [Fact]
    public void QueueDispatchAndRevocationHaveDeterministicLifetime()
    {
        using var runtime = new ScriptRuntime();
        runtime.Bindings.RegisterType<Counter>();
        using var session = runtime.CreateDevelopmentSession();
        var counter = new Counter();
        session.SetGlobal("counter", counter);
        Assert.True(session.Load("function on_event(value) counter:add(value) end").Succeeded);
        Assert.True(session.QueueEvent("on_event", 7));
        Assert.True(session.DispatchQueuedEvent(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(7, counter.Count);
        Assert.True(session.QueueEvent("on_event", 9));
        runtime.RevokeAll();
        Assert.False(session.DispatchQueuedEvent(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(7, counter.Count);
        Assert.True(session.IsRetired);
    }

    [Fact]
    public void QueueOverflowRetiresSessionAndDisposesSubscriptions()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession();
        var disposed = false;
        session.TrackResource(new Cleanup(() => disposed = true));
        Assert.True(session.Load("function on_event() end").Succeeded);
        for (var i = 0; i < 64; i++) Assert.True(session.QueueEvent("on_event"));
        Assert.False(session.QueueEvent("on_event"));
        Assert.True(disposed);
        Assert.True(session.IsRetired);
    }

    [Fact]
    public void OverflowCleanupCanRevokeRuntimeWithoutInvalidatingEventFanout()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession();
        using var other = runtime.CreateDevelopmentSession();
        Assert.True(session.Load("function on_event() end").Succeeded);
        session.TrackResource(new Cleanup(runtime.RevokeAll));
        for (var i = 0; i < 64; i++) runtime.QueueEvent("on_event");
        runtime.QueueEvent("on_event");
        Assert.True(session.IsRetired);
        Assert.True(other.IsRetired);
    }

    [Fact]
    public void SessionCountIsBoundedAndRetiredSlotsCanBeReused()
    {
        using var runtime = new ScriptRuntime();
        var first = runtime.CreateDevelopmentSession();
        for (var i = 1; i < 8; i++) runtime.CreateDevelopmentSession();
        Assert.Throws<InvalidOperationException>(() => runtime.CreateDevelopmentSession());
        first.Dispose();
        Assert.NotNull(runtime.CreateDevelopmentSession());
    }

    [Fact]
    public void RevocationCannotBeBypassedByCleanupCreatingAnotherSession()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession();
        Exception? rejected = null;
        session.TrackResource(new Cleanup(() =>
        {
            runtime.RevokeAll(); // Reentrant teardown is idempotent.
            try { runtime.CreateDevelopmentSession(); }
            catch (Exception exception) { rejected = exception; }
        }));
        runtime.RevokeAll();
        Assert.IsType<InvalidOperationException>(rejected);
        Assert.True(session.IsRetired);
        Assert.NotNull(runtime.CreateDevelopmentSession());
    }

    [Fact]
    public void WrongThreadDisposalCannotPoisonOwnerThreadCleanup()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession();
        Exception? rejected = null;
        var thread = new Thread(() => { try { runtime.Dispose(); } catch (Exception exception) { rejected = exception; } });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        Assert.IsType<InvalidOperationException>(rejected);
        Assert.False(session.IsRetired);
        runtime.Dispose();
        Assert.True(session.IsRetired);
    }

    [Fact]
    public void CrossThreadSessionCallsAreRejectedBeforeLuaRuns()
    {
        using var runtime = new ScriptRuntime();
        using var session = runtime.CreateDevelopmentSession();
        Exception? error = null;
        var thread = new Thread(() => { try { session.Load("return 1"); } catch (Exception exception) { error = exception; } });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        Assert.IsType<InvalidOperationException>(error);
        Assert.False(session.IsRetired);
    }

    [ScriptType("counter")]
    private sealed class Counter
    {
        public int Count { get; private set; }
        [ScriptVisible("add")]
        public void Add(int value) => Count += value;
    }

    [ScriptType]
    private sealed class Revoker(Action revoke)
    {
        public int Calls { get; private set; }
        [ScriptVisible]
        public void Revoke() { Calls++; revoke(); }
    }

    private sealed class Cleanup(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
