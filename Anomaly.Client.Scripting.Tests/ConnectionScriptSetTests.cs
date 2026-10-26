using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Anomaly.Client.Scripting.Events;
using Anomaly.Client.Scripting.Tests.EventFixtures.Valid;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ConnectionScriptSetTests
{
    [Fact]
    public void Sources_load_in_pack_and_address_order_before_the_host_publishes_readiness()
    {
        SignalsEvents.Reset();
        using var runtime = RuntimeWithFixtureEvents();
        using var set = new ConnectionScriptSet(runtime, () => true);
        var sources = new[]
        {
            Source("beta:pack", "mods/beta/a.lua", Body("beta-a")),
            Source("alpha:pack", "mods/alpha/z.lua", Body("alpha-z")),
            Source("alpha:pack", "mods/alpha/a.lua", Body("alpha-a"))
        };
        var publishedReady = false;
        Assert.False(set.IsHealthy);
        Assert.True(set.InitializeLocal(sources, approved: true, out var error), error);
        Assert.True(set.IsHealthy);
        var sessions = Sessions(runtime);
        Assert.Equal(new[] { "alpha-a", "alpha-z", "beta-a" }, sessions.Select(session => session.Invoke("Label").Value));
        Assert.All(sessions, session => Assert.Equal("", session.Invoke("Seen").Value));
        Assert.False(publishedReady);

        publishedReady = set.IsHealthy;
        SignalsEvents.Publish(new Payload { Text = "Ready" });
        foreach (var session in sessions) Assert.True(session.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.True(publishedReady);
        Assert.Equal(new[] { "alpha-a:Ready", "alpha-z:Ready", "beta-a:Ready" }, sessions.Select(session => session.Invoke("Seen").Value));
    }

    [Fact]
    public void No_script_inventory_needs_no_execution_approval_and_allocates_no_vm()
    {
        using var runtime = new ScriptRuntime();
        using var set = new ConnectionScriptSet(runtime, () => true);
        var before = runtime.MemoryTotalAllocatedBytes;
        Assert.True(set.InitializeLocal(Array.Empty<ScriptSource>(), approved: false, out var error), error);
        Assert.True(set.IsHealthy);
        Assert.Equal(before, runtime.MemoryTotalAllocatedBytes);
        Assert.Empty(Sessions(runtime));
    }

    [Fact]
    public void A_live_connection_retains_only_routing_identities_after_source_compilation()
    {
        using var runtime = new ScriptRuntime();
        var (set, source) = CreateSetWithCollectibleSourceRecord(runtime);
        using (set)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(source.IsAlive);
            Assert.True(set.IsHealthy);
            Assert.Equal(1d, Assert.Single(Sessions(runtime)).Invoke("Read").Value);
            GC.KeepAlive(set);
        }
    }

    [Fact]
    public void Canonical_reserved_local_pack_sources_are_accepted_by_the_shared_source_layer()
    {
        using var runtime = new ScriptRuntime();
        using var set = new ConnectionScriptSet(runtime, () => true);
        Assert.True(set.InitializeLocal(new[] { Source("anomaly:local", "mods/anomaly/start.lua", "return 1") }, true, out var error), error);
        Assert.True(set.IsHealthy);
        Assert.Single(Sessions(runtime));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-- Valid UTF-8: caf\u00e9 \u96ea\r\n\treturn 1\n")]
    public void Empty_and_strict_unicode_source_text_can_initialize(string text)
    {
        using var runtime = new ScriptRuntime();
        using var set = new ConnectionScriptSet(runtime, () => true);
        Assert.True(set.InitializeLocal(new[] { Source("fixture:pack", "mods/fixture/start.lua", text) }, true, out var error), error);
        Assert.True(set.IsHealthy);
        Assert.Single(Sessions(runtime));
    }

    [Fact]
    public void Revalidating_empty_source_does_not_charge_process_lifetime_encoding_singletons()
    {
        using var runtime = new ScriptRuntime();
        var source = Source("fixture:pack", "mods/fixture/start.lua", string.Empty);
        using var scope = runtime.EnterAllocationScope();
        var before = runtime.MemoryTotalAllocatedBytes;
        for (var index = 0; index < 64; index++) source.Validate();
        Assert.Equal(before, runtime.MemoryTotalAllocatedBytes);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Missing_approval_or_a_stale_connection_rejects_before_vm_allocation(bool approved, bool current)
    {
        using var runtime = new ScriptRuntime();
        using var set = new ConnectionScriptSet(runtime, () => current);
        var before = runtime.MemoryTotalAllocatedBytes;
        Assert.False(set.InitializeLocal(new[] { Source("fixture:pack", "mods/fixture/a.lua", "return 1") }, approved, out var error));
        Assert.NotEmpty(error);
        Assert.False(set.IsHealthy);
        Assert.Equal(before, runtime.MemoryTotalAllocatedBytes);
        Assert.Empty(Sessions(runtime));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("bytes")]
    [InlineData("unicode")]
    [InlineData("bom")]
    [InlineData("nul")]
    [InlineData("escape")]
    [InlineData("control")]
    [InlineData("delete")]
    [InlineData("unicode-control")]
    [InlineData("dump")]
    [InlineData("address")]
    [InlineData("owner")]
    public void The_entire_inventory_is_validated_before_the_first_script_runs(string invalidKind)
    {
        SignalsEvents.Reset();
        using var runtime = RuntimeWithFixtureEvents();
        using var set = new ConnectionScriptSet(runtime, () => true);
        var invalid = Source("fixture:pack", "mods/fixture/b.lua", "return 2");
        invalid = invalidKind switch
        {
            "hash" => invalid with { SourceSha256 = new string('0', 64) },
            "bytes" => invalid with { SourceBytes = invalid.SourceBytes - 1 },
            "unicode" => invalid with { Source = "\ud800", SourceBytes = 1 },
            "bom" => Source("fixture:pack", "mods/fixture/b.lua", "\ufeffreturn 2"),
            "nul" => Source("fixture:pack", "mods/fixture/b.lua", "--\0"),
            "escape" => Source("fixture:pack", "mods/fixture/b.lua", "--\u001b"),
            "control" => Source("fixture:pack", "mods/fixture/b.lua", "--\u0001"),
            "delete" => Source("fixture:pack", "mods/fixture/b.lua", "--\u007f"),
            "unicode-control" => Source("fixture:pack", "mods/fixture/b.lua", "--\u0085"),
            "dump" => Source("fixture:pack", "mods/fixture/b.lua", "--MoonSharp_dump_b64::"),
            "address" => invalid with { Address = "mods/fixture/../b.lua" },
            "owner" => invalid with { Address = "mods/other/b.lua" },
            _ => throw new InvalidOperationException()
        };
        Assert.False(set.InitializeLocal(new[] { Source("fixture:pack", "mods/fixture/a.lua", Body("first")), invalid }, true, out _));
        Assert.Empty(Sessions(runtime));
        Assert.Equal(0, SignalsEvents.Adds);
        Assert.False(set.IsHealthy);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("file")]
    [InlineData("aggregate")]
    [InlineData("duplicate")]
    public void Inventory_count_file_bytes_aggregate_bytes_and_duplicate_addresses_are_bounded(string kind)
    {
        using var runtime = new ScriptRuntime();
        using var set = new ConnectionScriptSet(runtime, () => true);
        var count = kind == "count" ? 9 : kind == "aggregate" ? 5 : kind == "duplicate" ? 2 : 1;
        var text = kind == "file" ? new string(' ', 65537) : kind == "aggregate" ? new string(' ', 65536) : "return 1";
        var sources = Enumerable.Range(0, count).Select(index =>
            Source("fixture:pack", "mods/fixture/" + (kind == "duplicate" ? 0 : index) + ".lua", text)).ToArray();
        Assert.False(set.InitializeLocal(sources, true, out var error));
        Assert.NotEmpty(error);
        Assert.Empty(Sessions(runtime));
        Assert.False(set.IsHealthy);
    }

    [Theory]
    [InlineData("error('bounded startup failure')", "ScriptError")]
    [InlineData("for i=1,1000000 do end", "CpuLimit")]
    [InlineData("local retained={}; for i=1,20000 do retained[i]={} end", "MemoryLimit")]
    public void A_later_bounded_startup_failure_rolls_back_every_vm_and_subscription(string failingSource, string expected)
    {
        SignalsEvents.Reset();
        using var runtime = RuntimeWithFixtureEvents();
        using var set = new ConnectionScriptSet(runtime, () => true);
        Assert.False(set.InitializeLocal(new[]
        {
            Source("fixture:pack", "mods/fixture/a.lua", Body("first")),
            Source("fixture:pack", "mods/fixture/b.lua", failingSource)
        }, true, out var error));
        Assert.Contains("mods/fixture/b.lua", error);
        Assert.Contains(expected, error);
        Assert.False(set.IsHealthy);
        Assert.Empty(Sessions(runtime));
        Assert.Equal(1, SignalsEvents.Adds);
        Assert.Equal(1, SignalsEvents.Removes);
        int getters = Payload.GetterCalls;
        SignalsEvents.Publish(new Payload());
        Assert.Equal(getters, Payload.GetterCalls);
    }

    [Fact]
    public void A_scope_change_during_startup_rolls_back_before_readiness()
    {
        SignalsEvents.Reset();
        using var runtime = RuntimeWithFixtureEvents();
        var current = true;
        using var set = new ConnectionScriptSet(runtime, () => current);
        SignalsEvents.DuringAdd = () => current = false;
        try
        {
            Assert.False(set.InitializeLocal(new[] { Source("fixture:pack", "mods/fixture/a.lua", Body("first")) }, true, out _));
            Assert.False(set.IsHealthy);
            Assert.Empty(Sessions(runtime));
            Assert.Equal(1, SignalsEvents.Removes);
        }
        finally { SignalsEvents.DuringAdd = null; }
    }

    [Fact]
    public void Reconnect_failure_does_not_leave_old_callbacks_and_disposed_sets_cannot_revoke_a_later_replacement()
    {
        SignalsEvents.Reset();
        using var runtime = RuntimeWithFixtureEvents();
        var current = true;
        using var first = new ConnectionScriptSet(runtime, () => current);
        Assert.True(first.InitializeLocal(new[] { Source("fixture:pack", "mods/fixture/a.lua", Body("old")) }, true, out var error), error);
        current = false;
        Assert.False(first.IsHealthy);
        first.Dispose();
        Assert.Equal(1, SignalsEvents.Removes);

        using var failed = new ConnectionScriptSet(runtime, () => true);
        Assert.False(failed.InitializeLocal(new[] { Source("fixture:pack", "mods/fixture/b.lua", "error('new failure')") }, true, out _));
        Assert.Empty(Sessions(runtime));
        failed.Dispose();
        using var replacement = new ConnectionScriptSet(runtime, () => true);
        Assert.True(replacement.InitializeLocal(new[] { Source("fixture:pack", "mods/fixture/c.lua", Body("new")) }, true, out error), error);
        first.Dispose();
        Assert.False(first.InitializeLocal(Array.Empty<ScriptSource>(), true, out _));
        Assert.True(replacement.IsHealthy);
    }

    [Fact]
    public void A_later_callback_fault_is_visible_as_an_unhealthy_connection_script_set()
    {
        SignalsEvents.Reset();
        using var runtime = RuntimeWithFixtureEvents();
        using var set = new ConnectionScriptSet(runtime, () => true);
        Assert.True(set.InitializeLocal(new[] { Source("fixture:pack", "mods/fixture/a.lua",
            "Events.Signals.Changed.Add(function() error('handler failed') end)") }, true, out var error), error);
        SignalsEvents.Publish(new Payload());
        runtime.Tick();
        Assert.False(set.IsHealthy);
        Assert.Equal(1, SignalsEvents.Removes);
    }

    private static string Body(string label) => "local seen=''; function Label() return '" + label + "' end; " +
        "function Seen() return seen end; Events.Signals.Changed.Add(function(a) seen='" + label + ":'..a.Text end)";

    private static ScriptSource Source(string packId, string address, string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return new ScriptSource(packId, address, source, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ConnectionScriptSet Set, WeakReference Source) CreateSetWithCollectibleSourceRecord(ScriptRuntime runtime)
    {
        var source = Source("fixture:pack", "mods/fixture/start.lua", "function Read() return 1 end");
        var set = new ConnectionScriptSet(runtime, () => true);
        Assert.True(set.InitializeLocal(new[] { source }, true, out var error), error);
        return (set, new WeakReference(source));
    }

    private static ScriptRuntime RuntimeWithFixtureEvents()
    {
        var runtime = new ScriptRuntime();
        var catalog = EventDiscoveryTests.Discover(runtime.Bindings);
        typeof(ScriptRuntime).GetField("_eventCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, catalog);
        return runtime;
    }

    private static ScriptSession[] Sessions(ScriptRuntime runtime) =>
        ((List<ScriptSession>)typeof(ScriptRuntime).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!).ToArray();
}
