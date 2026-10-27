using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Output;
using Anomaly.Client.Scripting.Remote;
using Anomaly.Client.Scripting.Remote.Isolation;
using Anomaly.Client.Scripting.Remote.Protocol;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

[CollectionDefinition(RemoteWorkerCollection.Name, DisableParallelization = true)]
public sealed class RemoteWorkerCollection : ICollectionFixture<RemoteWorkerFixture>
{
    public const string Name = "Real isolated Lua worker";
}

public sealed class WindowsWorkerFactAttribute : FactAttribute
{
    public WindowsWorkerFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            Skip = "Requires a real Windows x64 AppContainer/job worker; no in-process substitute is permitted.";
    }
}

public sealed class WindowsWorkerTheoryAttribute : TheoryAttribute
{
    public WindowsWorkerTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            Skip = "Requires a real Windows x64 AppContainer/job worker; no in-process substitute is permitted.";
    }
}

public sealed class RemoteWorkerFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AnomalyRemoteIsolationTests", Guid.NewGuid().ToString("N"));
    private bool _started;

    internal void RequireQualified()
    {
        if (!_started)
        {
            Directory.CreateDirectory(_root);
            RemoteWorkerFactory.BeginQualification(_root);
            _started = true;
        }
        PumpUntil(() => !RemoteWorkerFactory.IsQualifying, RemoteWorkerFactory.Tick,
            "Actual AppContainer qualification did not finish.", TimeSpan.FromSeconds(75));
        Assert.True(RemoteWorkerFactory.IsQualified, RemoteWorkerFactory.DisabledReason);
    }

    internal static void PumpUntil(Func<bool> done, Action tick, string failure, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed < (timeout ?? TimeSpan.FromSeconds(25)), failure);
            tick();
            Thread.Sleep(1); // Deliberately synchronous: actor and game capabilities stay on their owner thread.
        }
    }

    public void Dispose()
    {
        if (_started)
            PumpUntil(() => !RemoteWorkerFactory.IsQualifying, RemoteWorkerFactory.Tick,
                "Qualification must finish before fixture teardown.", TimeSpan.FromMinutes(3));
        WaitForSandboxCleanup();
        // Production owns ACL/profile teardown; delete only after its actual completion, not a timing guess.
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    internal static void WaitForSandboxCleanup()
    {
        var cleanup = WorkerSandboxProcess.DrainCleanupAsync();
        PumpUntil(() => cleanup.IsCompleted, () => { }, "Deferred sandbox cleanup did not complete.", TimeSpan.FromMinutes(3));
        cleanup.GetAwaiter().GetResult();
    }
}

[Collection(RemoteWorkerCollection.Name)]
public sealed class RemoteIsolationTests(RemoteWorkerFixture fixture)
{
    private const string Pack = "fixture:isolated";
    private static readonly string Index = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("isolated-approved-index"))).ToLowerInvariant();

    [WindowsWorkerFact]
    public void Direct_launcher_rejects_a_protected_descendant_grant_without_poisoning_global_qualification()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The ACL regression requires Windows.");
        // Deliberately do not call RequireQualified: an adversarial launch must not permanently fail the factory.
        var qualification = (RemoteWorkerFactory.IsQualified, RemoteWorkerFactory.IsQualifying, RemoteWorkerFactory.DisabledReason);
        var root = Path.Combine(Path.GetTempPath(), "AnomalyRemoteAclTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sentinel = new FileInfo(Path.Combine(root, "explicit-package-access.bin"));
        var originalBytes = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(sentinel.FullName, originalBytes);
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User!;
        var ownerAcl = new FileSecurity();
        ownerAcl.SetAccessRuleProtection(true, false);
        ownerAcl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        sentinel.SetAccessControl(ownerAcl);
        var packageGrant = new FileSystemAccessRule(new SecurityIdentifier("S-1-15-2-2"),
            FileSystemRights.ReadAndExecute, AccessControlType.Allow);
        var maliciousAcl = sentinel.GetAccessControl();
        maliciousAcl.AddAccessRule(packageGrant);
        sentinel.SetAccessControl(maliciousAcl);
        try
        {
            var payloadRoot = Path.GetDirectoryName(typeof(ScriptRuntime).Assembly.Location)!;
            var files = new[]
            {
                "Anomaly.Client.ScriptWorker.dll", "Anomaly.Client.ScriptWorker.deps.json",
                "Anomaly.Client.ScriptWorker.runtimeconfig.json", "Anomaly.MoonSharp.Interpreter.dll"
            }.Select(name => Path.Combine(payloadRoot, name)).ToArray();
            var options = new WorkerSandboxOptions(TrustedDotnet(), files[0], files, new[] { root });
            Assert.Throws<InvalidOperationException>(() =>
            {
                // If rejection regresses, immediately dispose the real escaped worker rather than leaking it.
                using var escaped = WorkerSandboxProcess.Start(options);
            });
            Assert.Equal(originalBytes, File.ReadAllBytes(sentinel.FullName));
            RemoteWorkerFixture.WaitForSandboxCleanup();

            // Positive control with the same real payload, executable and protected directory. Remove only our ACE.
            var repaired = sentinel.GetAccessControl();
            repaired.RemoveAccessRuleSpecific(packageGrant);
            sentinel.SetAccessControl(repaired);
            using (var sandbox = WorkerSandboxProcess.Start(options))
            {
                using var observed = Process.GetProcessById(sandbox.ProcessId);
                using (var channel = new RemoteWorkerChannel(sandbox))
                    Assert.Equal(FrameKind.Ready, Read(channel).Kind);
                AssertExited(observed);
            }
            Assert.Equal(qualification, (RemoteWorkerFactory.IsQualified, RemoteWorkerFactory.IsQualifying, RemoteWorkerFactory.DisabledReason));
        }
        finally
        {
            RemoteWorkerFixture.WaitForSandboxCleanup();
            if (sentinel.Exists) sentinel.SetAccessControl(ownerAcl);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [WindowsWorkerFact]
    public void Real_worker_has_verified_token_job_and_native_allocation_ceiling_and_dies_on_disposal()
    {
        fixture.RequireQualified(); // Includes independent protected-file, loopback and child-process denial probes.
        var launch = RemoteWorkerFactory.StartWorkerAsync();
        RemoteWorkerFixture.PumpUntil(() => launch.IsCompleted, () => { }, "Worker launch did not finish.");
        using var sandbox = launch.GetAwaiter().GetResult();
        using var observed = Process.GetProcessById(sandbox.ProcessId);
        using (var channel = new RemoteWorkerChannel(sandbox))
        {
            var evidence = sandbox.Evidence;
            Assert.True(evidence.AppContainer);
            Assert.Equal(0, evidence.NetworkCapabilities);
            Assert.Equal(1, evidence.ActiveProcessLimit);
            Assert.Equal(10, evidence.CpuRatePercent);
            Assert.Equal(256L * 1024 * 1024, evidence.ProcessMemoryLimitBytes);
            Assert.Equal(256L * 1024 * 1024, evidence.JobMemoryLimitBytes);
            Assert.Equal(FrameKind.Ready, Read(channel).Kind);
            channel.Arm(TimeSpan.FromSeconds(15));
            Assert.True(channel.TrySend(new RemoteFrame(FrameKind.Probe, 0, 1, 0,
                RemoteWorkerFactory.Record(("mode", WireValue.String("Memory"))))));
            var result = Read(channel);
            Assert.Equal(FrameKind.Completed, result.Kind);
            Assert.Equal(1, result.Command);
            Assert.Equal("Completed", result.Data.Required("status").RequireString());
            var proof = result.Data.Required("value");
            Assert.True(proof.Required("memoryDenied").RequireBoolean());
            Assert.InRange(proof.Required("allocatedBytes").RequireInteger(0, 512L * 1024 * 1024), 128L * 1024 * 1024, 256L * 1024 * 1024);
            Assert.InRange(evidence.PeakProcessMemoryBytes, 128L * 1024 * 1024, evidence.ProcessMemoryLimitBytes);
            Assert.False(observed.HasExited);
        }
        AssertExited(observed);
    }

    [WindowsWorkerFact]
    public void Independent_wall_watchdog_kills_a_real_idle_worker_without_any_actor_ticks()
    {
        fixture.RequireQualified();
        var launch = RemoteWorkerFactory.StartWorkerAsync();
        RemoteWorkerFixture.PumpUntil(() => launch.IsCompleted, () => { }, "Worker launch did not finish.");
        using var sandbox = launch.GetAwaiter().GetResult();
        using var observed = Process.GetProcessById(sandbox.ProcessId);
        using var channel = new RemoteWorkerChannel(sandbox);
        Assert.Equal(FrameKind.Ready, Read(channel).Kind);
        Assert.False(observed.HasExited);
        Assert.Null(channel.Fault);

        // No Lua is loaded, no command runs, and no actor is pumped. Only the independent wall watchdog can retire it.
        channel.Arm(TimeSpan.FromMilliseconds(200));
        RemoteWorkerFixture.PumpUntil(() => observed.HasExited, () => { },
            "The independent wall watchdog did not terminate the idle worker.", TimeSpan.FromSeconds(2));
        Assert.NotNull(channel.Fault);
        Assert.Contains("deadline", channel.Fault!, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsWorkerFact]
    public void Production_surface_executes_output_messages_frozen_events_and_retained_live_queries_without_a_local_VM()
    {
        fixture.RequireQualified();
        var logs = new List<string>();
        var sent = new List<ScriptToServerMessage>();
        using var sink = new RecordingSink();
        using var runtime = new ScriptRuntime(textSink: sink, outputLog: logs.Add);
        runtime.DiscoverApi();
        var phase = typeof(RoundEvents).GetProperty(nameof(RoundEvents.CurrentPhase))!;
        var original = RoundEvents.CurrentPhase;
        phase.SetValue(null, RoundPhase.WaitingForPlayers);
        try
        {
            using var actor = new Actor(runtime, () => true, sent, 7);
            actor.Initialize(Source("""
                assert(io==nil and os==nil and debug==nil and package==nil and require==nil)
                assert(load==nil and loadfile==nil and dofile==nil and coroutine==nil)
                assert(rawget==nil and rawset==nil and getmetatable==nil and setmetatable==nil)
                assert(Output.GetType==nil and Game.GetType==nil and Game.Assembly==nil)
                assert(not pcall(function() Game.IsRoundStarted=true end))
                assert(not Output:Log('invalid\nline'))
                assert(not Output:ShowText('invalid',100))
                assert(Output:Log('isolated ready'))
                assert(Output:ShowText('isolated display',10))
                local retainedGame=Game
                local retainedOutput=Output
                local snapshot=nil
                Events.Client.Ready.Add(function(args)
                    assert(retainedOutput==Output)
                    if snapshot==nil then
                        assert(args.SceneName=='first' and not retainedGame.IsRoundStarted)
                        snapshot=args
                        assert(not pcall(function() args.SceneName='overwrite' end))
                        assert(Messages.Send('Outbound',{Value=1}))
                    else
                        assert(snapshot.SceneName=='first' and args.SceneName=='second')
                        assert(retainedGame.IsRoundStarted)
                        assert(Messages.Send('Outbound',{Value=2}))
                    end
                end)
                Messages.On('Inbound',function(message)
                    assert(Messages.Send('Outbound',{Value=message.Value+100}))
                end)
                """));
            Assert.Single(logs);
            Assert.Contains("isolated ready", logs[0]);
            Assert.Equal("isolated display", Assert.Single(sink.Visible.Values));
            AssertNoLocalVm(runtime);
            PublishReady("first");
            actor.Until(() => sent.Count == 1);
            phase.SetValue(null, RoundPhase.InProgress);
            PublishReady("second");
            actor.Until(() => sent.Count == 2);
            Assert.True(actor.Set.Receive(Inbound(23, 7)));
            actor.Until(() => sent.Count == 3);
            Assert.Equal(new[] { 1, 2, 123 }, sent.Select(message => (int)message.Payload.Values["Value"]));
            Assert.All(sent, message =>
            {
                Assert.Equal(Pack, message.PackId);
                Assert.Equal(Index, message.IndexSha256);
                Assert.Equal(7UL, message.ActivationId);
            });
            Assert.True(actor.Set.IsHealthy, actor.Set.FailureReason);
            actor.Set.Dispose();
            Assert.Empty(sink.Visible);
            AssertExited(actor.Worker);
        }
        finally { phase.SetValue(null, original); }
    }

    [WindowsWorkerTheory]
    [InlineData("while true do end")]
    [InlineData("pcall(function() while true do end end)")]
    [InlineData("xpcall(function() while true do end end,function() return 'caught' end)")]
    public void Terminal_guest_limits_escape_protected_calls_and_revoke_every_owner(string body)
    {
        fixture.RequireQualified();
        var sent = new List<ScriptToServerMessage>();
        using var sink = new RecordingSink();
        using var runtime = new ScriptRuntime(textSink: sink);
        runtime.DiscoverApi();
        using var actor = new Actor(runtime, () => true, sent, 7);
        actor.Initialize(Source("assert(Output:ShowText('owned',10)); Messages.On('Inbound',function() " + body +
            "; Messages.Send('Outbound',{Value=999}) end)"), Source("assert(Output:ShowText('sibling',10)); Events.Client.Ready.Add(function() Messages.Send('Outbound',{Value=777}) end)", "fixture:second", "mods/fixture/second.lua"));
        Assert.Equal(2, sink.Visible.Count);
        Assert.True(actor.Set.Receive(Inbound(1, 7)));
        actor.Until(() => actor.Set.FailureReason.Length != 0, requireHealthy: false);
        Assert.False(actor.Set.Receive(Inbound(2, 7)));
        PublishReady("after terminal fault");
        actor.Set.Tick();
        Assert.Empty(sent);
        Assert.Empty(sink.Visible);
        AssertNoLocalVm(runtime);
        Assert.True(actor.Set.FailureReason.Contains("CpuLimit", StringComparison.Ordinal) ||
            actor.Set.FailureReason.Contains("Deadline", StringComparison.Ordinal), actor.Set.FailureReason);
        AssertExited(actor.Worker);
        Assert.Equal(1, actor.Completions);
    }

    [WindowsWorkerFact]
    public void Retained_guest_memory_exhaustion_is_terminal_across_callbacks_and_protected_calls()
    {
        fixture.RequireQualified();
        var sent = new List<ScriptToServerMessage>();
        using var sink = new RecordingSink();
        using var runtime = new ScriptRuntime(textSink: sink);
        runtime.DiscoverApi();
        using var actor = new Actor(runtime, () => true, sent, 7);
        actor.Initialize(Source("""
            local held={}
            assert(Output:ShowText('owned',10))
            Messages.On('Inbound',function(message)
                pcall(function() held[#held+1]=string.rep('x',32768) end)
                assert(Messages.Send('Outbound',{Value=message.Value}))
            end)
            """), Source("assert(Output:ShowText('sibling',10))", "fixture:second", "mods/fixture/second.lua"));
        for (int request = 1; request <= 512 && actor.Set.FailureReason.Length == 0; request++)
        {
            Assert.True(actor.Set.Receive(Inbound(request, 7)));
            int expected = request;
            actor.Until(() => sent.Count == expected || actor.Set.FailureReason.Length != 0, requireHealthy: false);
        }
        Assert.Contains("MemoryLimit", actor.Set.FailureReason);
        Assert.False(actor.Set.IsHealthy);
        Assert.False(actor.Set.Receive(Inbound(999, 7)));
        Assert.Empty(sink.Visible);
        AssertNoLocalVm(runtime);
        AssertExited(actor.Worker);
    }

    [WindowsWorkerFact]
    public void External_worker_exit_retires_text_subscriptions_and_transport_authority()
    {
        fixture.RequireQualified();
        var sent = new List<ScriptToServerMessage>();
        using var sink = new RecordingSink();
        using var runtime = new ScriptRuntime(textSink: sink);
        runtime.DiscoverApi();
        using var actor = new Actor(runtime, () => true, sent, 7);
        actor.Initialize(Source("assert(Output:ShowText('owned',10)); Events.Client.Ready.Add(function() Messages.Send('Outbound',{Value=99}) end); Messages.On('Inbound',function() end)"));
        actor.Worker.Kill();
        actor.Until(() => actor.Set.FailureReason.Length != 0, requireHealthy: false);
        PublishReady("after exit");
        Assert.False(actor.Set.Receive(Inbound(1, 7)));
        Assert.Empty(sent);
        Assert.Empty(sink.Visible);
        AssertExited(actor.Worker);
        Assert.Equal(1, actor.Completions);
    }

    [WindowsWorkerFact]
    public void Revoked_queued_callbacks_cannot_cross_a_replacement_activation_or_clear_its_output()
    {
        fixture.RequireQualified();
        var current = true;
        var oldSent = new List<ScriptToServerMessage>();
        var newSent = new List<ScriptToServerMessage>();
        using var sink = new RecordingSink();
        using var runtime = new ScriptRuntime(textSink: sink);
        runtime.DiscoverApi();
        const string source = "assert(Output:ShowText('active',10)); Messages.On('Inbound',function(m) assert(Messages.Send('Outbound',{Value=m.Value})) end); Events.Client.Ready.Add(function() assert(Messages.Send('Outbound',{Value=42})) end)";
        using var old = new Actor(runtime, () => current, oldSent, 7);
        old.Initialize(Source(source));
        Assert.True(old.Set.Receive(Inbound(1, 7)));
        PublishReady("queued before revoke");
        current = false;
        old.Set.Tick();
        Assert.False(old.Set.IsHealthy);
        Assert.Empty(sink.Visible);
        AssertExited(old.Worker);
        using var replacement = new Actor(runtime, () => true, newSent, 8);
        replacement.Initialize(Source(source));
        Assert.False(replacement.Set.Receive(Inbound(2, 7)));
        Assert.False(old.Set.Receive(Inbound(3, 7)));
        old.Set.Dispose();
        old.Set.Tick();
        Assert.Equal("active", Assert.Single(sink.Visible.Values));
        Assert.True(replacement.Set.Receive(Inbound(4, 8)));
        replacement.Until(() => newSent.Count == 1);
        PublishReady("only replacement");
        replacement.Until(() => newSent.Count == 2);
        Assert.Empty(oldSent);
        Assert.Equal(new[] { 4, 42 }, newSent.Select(message => (int)message.Payload.Values["Value"]));
        Assert.All(newSent, message => Assert.Equal(8UL, message.ActivationId));
        Assert.Equal(1, old.Completions);
        Assert.Equal(1, replacement.Completions);
    }

    [WindowsWorkerFact]
    public void Distinct_sources_in_one_pack_receive_the_same_message_and_event_in_separate_worker_VMs()
    {
        fixture.RequireQualified();
        var sent = new List<ScriptToServerMessage>();
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        using var actor = new Actor(runtime, () => true, sent, 7);
        const string subscriptions = """
            Messages.On('Inbound',function(message)
                assert(Messages.Send('Outbound',{Value=marker*100+message.Value}))
            end)
            Events.Client.Ready.Add(function(args)
                assert(args.SceneName=='shared')
                assert(Messages.Send('Outbound',{Value=marker*1000}))
            end)
            """;
        // Reverse inventory order deliberately; every source retains its own globals and registrations.
        actor.Initialize(
            Source("assert(marker==nil); marker=2; " + subscriptions, address: "mods/fixture/z.lua"),
            Source("assert(marker==nil); marker=1; " + subscriptions, address: "mods/fixture/a.lua"));
        Assert.True(actor.Set.Receive(Inbound(31, 7)));
        actor.Until(() => sent.Count == 2);
        Assert.Equal(new[] { 131, 231 }, sent.Select(message => (int)message.Payload.Values["Value"]).OrderBy(value => value));
        PublishReady("shared");
        actor.Until(() => sent.Count == 4);
        Assert.Equal(new[] { 131, 231, 1000, 2000 }, sent.Select(message => (int)message.Payload.Values["Value"]).OrderBy(value => value));
        Assert.All(sent, message =>
        {
            Assert.Equal(Pack, message.PackId);
            Assert.Equal(Index, message.IndexSha256);
            Assert.Equal(7UL, message.ActivationId);
        });
        Assert.True(actor.Set.IsHealthy, actor.Set.FailureReason);
        AssertNoLocalVm(runtime);
        actor.Set.Dispose();
        AssertExited(actor.Worker);
    }

    [Theory]
    [InlineData("field type")]
    [InlineData("direction")]
    [InlineData("index")]
    [InlineData("missing contracts")]
    public void Approval_cannot_admit_inconsistent_contracts_between_sources_of_one_pack(string mismatch)
    {
        var logs = new List<string>();
        using var runtime = new ScriptRuntime(outputLog: logs.Add);
        var first = Source("Output:Log('must never execute')", address: "mods/fixture/a.lua");
        var second = Source("return 1", address: "mods/fixture/z.lua");
        switch (mismatch)
        {
            case "field type": second.MessageSchemas![0].Fields[0].Type = "Boolean"; break;
            case "direction": second.MessageSchemas![0].Direction = AnomalyMessageDirection.ClientToServer; break;
            case "index":
                second = second with { IndexSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("different-approved-index"))).ToLowerInvariant() };
                break;
            case "missing contracts": second = second with { MessageSchemas = null }; break;
        }
        first.Validate();
        second.Validate();
        using var set = new RemoteConnectionScriptSet(runtime, () => true, activationId: 7);
        var owner = Environment.CurrentManagedThreadId;
        bool? accepted = null;
        var completions = 0;
        var reason = "";
        set.BeginInitialize(new[] { second, first }, approved: true, (ok, error) =>
        {
            Assert.Equal(owner, Environment.CurrentManagedThreadId);
            accepted = ok;
            reason = error;
            completions++;
        });
        // No Tick, qualification or worker launch is needed to reject contradictory approved authority.
        Assert.True(accepted.HasValue, "Contract rejection must complete synchronously before launching a worker.");
        Assert.False(accepted.GetValueOrDefault(true));
        Assert.Contains("contracts", reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(set.IsInitializing);
        Assert.False(set.IsHealthy);
        set.Tick();
        Assert.Equal(1, completions);
        Assert.Empty(logs);
        AssertNoLocalVm(runtime);
    }

    [WindowsWorkerFact]
    public void Failed_required_pack_load_rolls_back_previously_created_output_and_callbacks()
    {
        fixture.RequireQualified();
        var sent = new List<ScriptToServerMessage>();
        using var sink = new RecordingSink();
        using var runtime = new ScriptRuntime(textSink: sink);
        runtime.DiscoverApi();
        using var actor = new Actor(runtime, () => true, sent, 7);
        actor.Initialize(false, Source("assert(Output:ShowText('not committed',10)); Events.Client.Ready.Add(function() Messages.Send('Outbound',{Value=1}) end)"),
            Source("error('required pack failure')", "fixture:second", "mods/fixture/second.lua"));
        Assert.Contains("required pack failure", actor.Set.FailureReason);
        Assert.False(actor.Set.IsHealthy);
        Assert.Empty(sink.Visible);
        PublishReady("after rollback");
        actor.Set.Tick();
        Assert.Empty(sent);
        AssertNoLocalVm(runtime);
        AssertExited(actor.Worker);
        Assert.Equal(1, actor.Completions);
    }

    [WindowsWorkerFact]
    public void Qualification_does_not_admit_raw_source_or_arbitrary_development_CLR_policies()
    {
        fixture.RequireQualified();
        using var runtime = new ScriptRuntime();
        Assert.Throws<NotSupportedException>(() => runtime.CreateRemoteSession("return 1"));
        runtime.Bindings.RegisterType<ArbitraryFixture>();
        using var actor = new Actor(runtime, () => true, new List<ScriptToServerMessage>(), 7);
        actor.Initialize(false, Source("return 1"));
        Assert.False(actor.Set.IsHealthy);
        AssertNoLocalVm(runtime);
        Assert.Equal(1, actor.Completions);
    }

    private static string TrustedDotnet()
    {
        for (var directory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()); directory != null; directory = directory.Parent)
        {
            var executable = Path.Combine(directory.FullName, "dotnet.exe");
            if (File.Exists(executable) && Directory.Exists(Path.Combine(directory.FullName, "shared", "Microsoft.NETCore.App")))
                return executable;
        }
        throw new FileNotFoundException("The direct-launch regression requires the loaded runtime's trusted machine dotnet host.");
    }

    private static RemoteFrame Read(RemoteWorkerChannel channel)
    {
        RemoteFrame? result = null;
        RemoteWorkerFixture.PumpUntil(() => result != null, () =>
        {
            Assert.Null(channel.Fault);
            if (channel.TryRead(out var frame)) result = frame;
        }, "Worker did not respond.");
        return result!;
    }

    private static ScriptSource Source(string source, string pack = Pack, string address = "mods/fixture/start.lua")
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return new ScriptSource(pack, address, source,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length, new[]
            {
                new ScriptMessageSchema { Name = "Inbound", Direction = AnomalyMessageDirection.ServerToClient,
                    Fields = new() { new() { Name = "Value", Type = "Int32" } } },
                new ScriptMessageSchema { Name = "Outbound", Direction = AnomalyMessageDirection.ClientToServer,
                    Fields = new() { new() { Name = "Value", Type = "Int32" } } }
            }, Index);
    }

    private static ScriptToClientMessage Inbound(int value, ulong activation) => ScriptToClientMessage.Create(Pack, "Inbound",
        ScriptMessagePayload.FromValues(new Dictionary<string, object> { ["Value"] = value })).Bind(Index, activation);

    // Client.Ready is the production primitive-only event. SceneLoaded.SceneName dereferences a native Unity Scene,
    // so SceneLoaded and non-root native live-proxy lifetime coverage belong in the actual game smoke harness.
    private static void PublishReady(string scene) => typeof(ClientEvents).GetMethod("OnReady", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, new object[] { new ClientReadyEventArgs(scene) });

    private static void AssertNoLocalVm(ScriptRuntime runtime) => Assert.Empty((List<ScriptSession>)typeof(ScriptRuntime)
        .GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime)!);

    private static void AssertExited(Process process) => RemoteWorkerFixture.PumpUntil(() => process.HasExited, () => { },
        "The retired actor's real worker process survived revocation.", TimeSpan.FromSeconds(10));

    [ScriptType("ArbitraryFixture")]
    private sealed class ArbitraryFixture
    {
        [ScriptVisible] public string ReadFile(string path) => File.ReadAllText(path);
    }

    private sealed class RecordingSink : IScriptTextSink
    {
        internal Dictionary<int, string> Visible { get; } = new();
        public bool Set(int slot, string plainText) { Visible[slot] = plainText; return true; }
        public void Clear(int slot) => Visible.Remove(slot);
        public void Dispose() => Visible.Clear();
    }

    private sealed class Actor : IDisposable
    {
        private readonly int _owner = Environment.CurrentManagedThreadId;
        private Process? _worker;
        internal RemoteConnectionScriptSet Set { get; }
        internal Process Worker => _worker ?? throw new InvalidOperationException("No real worker was observed.");
        internal int Completions { get; private set; }

        internal Actor(ScriptRuntime runtime, Func<bool> current, List<ScriptToServerMessage> sent, ulong activation)
        {
            Set = new RemoteConnectionScriptSet(runtime, current, message =>
            {
                Assert.Equal(_owner, Environment.CurrentManagedThreadId);
                sent.Add(message);
                return true;
            }, activation);
        }

        internal void Initialize(params ScriptSource[] sources) => Initialize(true, sources);
        internal void Initialize(bool expectedSuccess, params ScriptSource[] sources)
        {
            bool? success = null;
            string error = "";
            Set.BeginInitialize(sources, true, (ok, reason) =>
            {
                Assert.Equal(_owner, Environment.CurrentManagedThreadId);
                Completions++;
                success = ok;
                error = reason;
            });
            RemoteWorkerFixture.PumpUntil(() => success.HasValue, Tick, "Actor initialization did not complete: " + error);
            Assert.True(success == expectedSuccess, error);
            Assert.Equal(1, Completions);
            Assert.Equal(expectedSuccess, Set.IsHealthy);
        }

        internal void Until(Func<bool> done, bool requireHealthy = true) => RemoteWorkerFixture.PumpUntil(done, () =>
        {
            Tick();
            if (requireHealthy) Assert.True(Set.IsHealthy, Set.FailureReason);
        }, "Actor did not reach the expected observable result.");

        private void CaptureWorker()
        {
            if (_worker != null) return;
            var channel = (RemoteWorkerChannel?)typeof(RemoteConnectionScriptSet)
                .GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Set);
            if (channel != null) _worker = Process.GetProcessById(channel.Process.ProcessId);
        }

        private void Tick() { CaptureWorker(); Set.Tick(); CaptureWorker(); }
        public void Dispose() { Set.Dispose(); _worker?.Dispose(); }
    }
}
