using System.Reflection;
using Anomaly.Client.Scripting.Messaging;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Scripting;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class RuntimeMemoryHardeningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Revalidating_empty_sources_has_no_unaccounted_identity_or_schema_allocations(bool withSchemas)
    {
        var schemas = withSchemas ? Enumerable.Range(0, ScriptMessageSchema.MaxSchemas).Select(index => new ScriptMessageSchema
        {
            Name = "Message" + index,
            Direction = AnomalyMessageDirection.ServerToClient,
            Fields = Enumerable.Range(0, ScriptMessageSchema.MaxFields)
                .Select(field => new ScriptMessageField { Name = "Field" + field, Type = "Boolean" }).ToList()
        }).ToArray() : null;
        var source = EmptySource() with { MessageSchemas = schemas, IndexSha256 = schemas == null ? null : new string('0', 64) };
        using var runtime = new ScriptRuntime();
        using var scope = runtime.EnterAllocationScope();
        for (var index = 0; index < 16; index++) source.Validate();
        var total = runtime.MemoryTotalAllocatedBytes;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 64; index++) source.Validate();
        var actual = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(0, actual);
        Assert.Equal(total, runtime.MemoryTotalAllocatedBytes);
    }

    [Theory]
    [InlineData("fixture:pack", "mods/fixture/start.lua")]
    [InlineData("anomaly:local", "mods/anomaly/start.lua")]
    [InlineData("a.b_c:pack-1.2", "mods/a.b_c/nested/start.lua")]
    [InlineData("fixture:pack", "mods/fixture/.lua")]
    [InlineData("fixture:pack", "mods/other/start.lua")]
    [InlineData("fixture:pack", "mods/fixture//start.lua")]
    [InlineData("fixture:pack", "mods/fixture/./start.lua")]
    [InlineData("fixture:pack", "mods/fixture/../start.lua")]
    [InlineData("fixture:pack", "mods/fixture/start.Lua")]
    [InlineData("fixture:pack", "mods/fixture/start.lua/")]
    [InlineData("fixture:pack", "game/skin/logo")]
    [InlineData("fixture.:pack", "mods/fixture./start.lua")]
    [InlineData("fixture:..", "mods/fixture/start.lua")]
    [InlineData("fixture:a:b", "mods/fixture/start.lua")]
    [InlineData("1fixture:pack", "mods/1fixture/start.lua")]
    [InlineData("fixture._bad:pack", "mods/fixture._bad/start.lua")]
    public void Allocation_free_identity_check_preserves_the_wire_rules(string packId, string address)
    {
        var expected = AssetPackIndexCodec.IsPackId(packId, remote: false) &&
            AssetAddress.TryParse(address, out var parsed) && !parsed.IsGameReplacement &&
            address.StartsWith("mods/" + packId[..packId.IndexOf(':')] + "/", StringComparison.Ordinal) &&
            address.EndsWith(".lua", StringComparison.Ordinal);
        var source = EmptySource() with { PackId = packId, Address = address };
        if (expected) source.Validate();
        else Assert.Throws<InvalidDataException>(source.Validate);
    }

    [Fact]
    public void Failed_capability_installation_does_not_publish_or_retain_an_active_session()
    {
        using var runtime = new ScriptRuntime();
        var schema = new ScriptMessageSchema { Name = "Duplicate", Direction = AnomalyMessageDirection.ServerToClient };
        var context = new ScriptMessageContext("fixture:pack", new string('0', 64), 0,
            new[] { schema, schema }, () => true, null);
        for (var attempt = 0; attempt < 9; attempt++)
        {
            Assert.Throws<ArgumentException>(() => runtime.CreateLocalSession(EmptySource(), messageContext: context));
            Assert.Empty(Sessions(runtime));
        }
        using var session = runtime.CreateLocalSession(EmptySource());
        Assert.True(session.Load(string.Empty).Succeeded);
        Assert.Single(Sessions(runtime));
    }

    [Fact]
    public void Local_source_factory_checks_thread_ownership_before_source_metadata()
    {
        using var runtime = new ScriptRuntime();
        Exception? failure = null;
        var before = runtime.MemoryTotalAllocatedBytes;
        var foreign = new Thread(() =>
        {
            try { runtime.CreateLocalSession(null!); }
            catch (Exception exception) { failure = exception; }
        });
        foreign.Start();
        Assert.True(foreign.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(before, runtime.MemoryTotalAllocatedBytes);
        Assert.Empty(Sessions(runtime));
    }

    [Fact]
    public void Retired_session_wrappers_drop_the_message_capability_and_its_vm_reference()
    {
        using var runtime = new ScriptRuntime();
        var context = new ScriptMessageContext("fixture:pack", new string('0', 64), 0,
            Array.Empty<ScriptMessageSchema>(), () => true, null);
        using var session = runtime.CreateLocalSession(EmptySource(), messageContext: context);
        var capability = typeof(ScriptSession).GetField("_messages", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.NotNull(capability.GetValue(session));
        Assert.True(session.Load(string.Empty).Succeeded);
        session.Dispose();
        Assert.True(session.IsRetired);
        Assert.Null(capability.GetValue(session));
        Assert.Null(typeof(ScriptSession).GetField("_script", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session));
        Assert.Equal(0, session.QueuedCount);
        Assert.Equal(0, session.QueuedMessageBytes);
    }

    private static IReadOnlyList<ScriptSession> Sessions(ScriptRuntime runtime) =>
        (List<ScriptSession>)typeof(ScriptRuntime).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;

    private static ScriptSource EmptySource() => new("fixture:pack", "mods/fixture/start.lua", string.Empty,
        ScriptSourceValidator.EmptySha256Hex, 0);
}
