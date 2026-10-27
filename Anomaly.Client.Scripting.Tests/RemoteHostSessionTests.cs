using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Remote;
using Anomaly.Client.Scripting.Remote.Protocol;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class RemoteHostSessionTests
{
    private const string Pack = "fixture:pack";
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";


    [Fact]
    public void Development_fixture_policies_never_enter_the_remote_catalog_or_session()
    {
        using var runtime = new ScriptRuntime();
        runtime.Bindings.RegisterType<Fixture>();
        Assert.Throws<InvalidDataException>(() => RemoteHostSession.ExportCatalog(runtime));
        Assert.Throws<InvalidDataException>(() => new RemoteHostSession(runtime, Source(), 7, () => true, null));
        using var local = runtime.CreateDevelopmentSession();
        Assert.True(local.Load("return 1").Succeeded);
    }

    [Fact]
    public void Remote_output_reuses_validation_and_quotas_without_allocating_a_local_VM()
    {
        var logs = new List<string>();
        using var runtime = new ScriptRuntime(outputLog: logs.Add);
        using var host = new RemoteHostSession(runtime, Source(), 7, () => true, null);
        Assert.False(Call(host, "Log", WireValue.Numeric(1)).Boolean);
        Assert.False(Call(host, "Log", WireValue.String("injected\nline")).Boolean);
        for (var index = 0; index < 4; index++) Assert.True(Call(host, "Log", WireValue.String("literal")).Boolean);
        Assert.False(Call(host, "Log", WireValue.String("rate denied")).Boolean);
        Assert.Equal(4, logs.Count);
        Assert.Empty(LocalSessions(runtime));
    }

    [Fact]
    public void Message_grants_are_frozen_and_parent_binds_transport_scope()
    {
        var schemas = Schemas();
        var sent = new List<ScriptToServerMessage>();
        using var runtime = new ScriptRuntime();
        using var host = new RemoteHostSession(runtime, Source(schemas), 7, () => true, message => { sent.Add(message); return true; });
        schemas[0].Name = "Replaced";
        schemas[1].Fields[0].Type = "Boolean";
        var token = Result(host, Request("MessageOn", ("name", WireValue.String("Inbound")))).Text;
        Assert.True(host.Receive(Inbound(1)));
        Assert.True(host.TryDequeue(out var delivery));
        Assert.Equal(token, delivery.Token);
        Assert.Equal(1, delivery.Arguments.Items[0].Required("Value").Number);
        var payload = RemoteHostValues.Record(("Value", WireValue.Numeric(9)));
        var sentResult = Result(host, Request("Send", ("name", WireValue.String("Outbound")), ("payload", payload)));
        Assert.True(sentResult.Items[0].Boolean);
        var envelope = Assert.Single(sent);
        Assert.Equal(Pack, envelope.PackId);
        Assert.Equal(Hash, envelope.IndexSha256);
        Assert.Equal(7UL, envelope.ActivationId);
        Assert.Equal(9, envelope.Payload.Values["Value"]);
        Assert.Empty(LocalSessions(runtime));
    }

    [Fact]
    public void Message_removal_invalidates_queued_generations_and_rejects_wrong_authority()
    {
        using var runtime = new ScriptRuntime();
        using var host = new RemoteHostSession(runtime, Source(Schemas()), 7, () => true, null);
        var token = Result(host, Request("MessageOn", ("name", WireValue.String("Inbound")))).Text;
        Assert.False(host.Receive(Inbound(1).Bind(Hash, 8)));
        Assert.True(host.Receive(Inbound(2)));
        Assert.True(Result(host, Request("MessageOff", ("token", WireValue.String(token)))).Boolean);
        Assert.False(host.TryDequeue(out _));
        Assert.True(host.IsHealthy);
    }

    [Fact]
    public void Combined_subscriptions_and_parent_message_queue_fail_closed()
    {
        using var runtime = new ScriptRuntime();
        using var host = new RemoteHostSession(runtime, Source(Schemas()), 7, () => true, null);
        Result(host, Request("MessageOn", ("name", WireValue.String("Inbound"))));
        for (var index = 0; index < 64; index++) Assert.True(host.Receive(Inbound(index)));
        Assert.False(host.Receive(Inbound(65)));
        Assert.False(host.IsHealthy);
        Assert.False(host.TryDequeue(out _));
        using var second = new RemoteHostSession(runtime, Source(Schemas()), 7, () => true, null);
        for (var index = 0; index < 32; index++) Result(second, Request("MessageOn", ("name", WireValue.String("Inbound"))));
        Assert.Throws<InvalidDataException>(() => second.Dispatch(Request("MessageOn", ("name", WireValue.String("Inbound")))));
        Assert.False(second.IsHealthy);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("root release")]
    [InlineData("invented handle")]
    [InlineData("release bound")]
    public void Malformed_host_requests_retire_the_owner(string kind)
    {
        using var runtime = new ScriptRuntime();
        using var host = new RemoteHostSession(runtime, Source(), 7, () => true, null);
        var request = kind switch
        {
            "unknown" => Request("Set"),
            "root release" => RemoteHostValues.Record(("op", WireValue.String("MessageOff")), ("token", WireValue.String(new string('a', 32))),
                ("release", WireValue.Array(new[] { WireValue.Numeric(2) }))),
            "release bound" => RemoteHostValues.Record(("op", WireValue.String("MessageOff")), ("token", WireValue.String(new string('a', 32))),
                ("release", WireValue.Array(Enumerable.Range(0, 65).Select(index => WireValue.Numeric(index + 3)).ToArray()))),
            _ => Request("Get", ("target", WireValue.Numeric(999)), ("member", WireValue.String("Log")),
                ("args", WireValue.Array(Array.Empty<WireValue>())))
        };
        Assert.Throws<InvalidDataException>(() => host.Dispatch(request));
        Assert.False(host.IsHealthy);
    }

    [Fact]
    public void Local_and_remote_subscribers_share_one_capture_and_removal_drops_remote_queue()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        using var local = runtime.CreateDevelopmentSession();
        Assert.True(local.Load("seen=''; Events.Client.Ready.Add(function(args) seen=args.SceneName end); function Read() return seen end").Succeeded);
        using var remote = new RemoteHostSession(runtime, Source(), 7, () => true, null);
        var token = Result(remote, Request("Subscribe", ("event", WireValue.String("Client.Ready")))).Text;
        PublishReady("first");
        Assert.True(remote.TryDequeue(out var first));
        Assert.Equal(token, first.Token);
        Assert.Equal("ClientReadyEventArgs", first.Arguments.Items[0].Text);
        Assert.Equal("first", first.Arguments.Items[0].Required("SceneName").Text);
        Assert.True(local.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal("first", local.Invoke("Read").Value);
        PublishReady("removed");
        Assert.True(Result(remote, Request("Unsubscribe", ("token", WireValue.String(token)))).Boolean);
        Assert.False(remote.TryDequeue(out _));
        Assert.True(local.DispatchQueuedEvent(TimeSpan.FromSeconds(1)));
        Assert.Equal("removed", local.Invoke("Read").Value);
    }

    [Fact]
    public void Foreign_publishers_and_stale_connection_scope_never_dispatch_remote_callbacks()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        var current = true;
        using var remote = new RemoteHostSession(runtime, Source(), 7, () => current, null);
        Result(remote, Request("Subscribe", ("event", WireValue.String("Client.Ready"))));
        var publisher = new Thread(() => PublishReady("foreign"));
        publisher.Start();
        Assert.True(publisher.Join(TimeSpan.FromSeconds(2)));
        Assert.False(remote.IsHealthy);
        Assert.False(remote.TryDequeue(out _));
        using var replacement = new RemoteHostSession(runtime, Source(Schemas()), 8, () => current, null);
        current = false;
        Assert.False(replacement.IsHealthy);
        Assert.False(replacement.TryDequeue(out _));
    }

    [Fact]
    public void Coalesced_remote_events_keep_the_latest_snapshot_and_remove_the_old_generation()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        using var remote = new RemoteHostSession(runtime, Source(), 7, () => true, null);
        var token = Result(remote, Request("Subscribe", ("event", WireValue.String("Stats.StaminaChanged")))).Text;
        var publish = typeof(StatsEvents).GetMethod("OnStaminaChanged", BindingFlags.NonPublic | BindingFlags.Static)!;
        for (var index = 0; index < 100; index++)
            publish.Invoke(null, new object[] { new PlayerStaminaChangedEventArgs(null!, index, index + 1) });
        Assert.True(remote.IsHealthy);
        Assert.True(remote.TryDequeue(out var delivery));
        Assert.Equal(100, delivery.Arguments.Items[0].Required("Current").Number);
        Assert.False(remote.TryDequeue(out _));
        publish.Invoke(null, new object[] { new PlayerStaminaChangedEventArgs(null!, 100, 101) });
        Assert.True(Result(remote, Request("Unsubscribe", ("token", WireValue.String(token)))).Boolean);
        Assert.False(remote.TryDequeue(out _));
    }

    [Fact]
    public void Snapshot_string_limits_retire_remote_publication_owners()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        using var remote = new RemoteHostSession(runtime, Source(), 7, () => true, null);
        Result(remote, Request("Subscribe", ("event", WireValue.String("Client.Ready"))));
        PublishReady(new string('x', 4097));
        Assert.False(remote.IsHealthy);
        Assert.False(remote.TryDequeue(out _));
    }


    private static WireValue Call(RemoteHostSession host, string name, params WireValue[] arguments) => Result(host,
        Request("Call", ("target", WireValue.Numeric(2)), ("member", WireValue.String(name)), ("args", WireValue.Array(arguments))));
    private static WireValue Result(RemoteHostSession host, WireValue request)
    {
        var response = host.Dispatch(request);
        Assert.True(response.Required("ok").Boolean);
        return response.Required("value");
    }
    private static WireValue Request(string operation, params (string Key, WireValue Value)[] fields)
    {
        var values = new Dictionary<string, WireValue>(StringComparer.Ordinal)
            { ["op"] = WireValue.String(operation), ["release"] = WireValue.Array(Array.Empty<WireValue>()) };
        foreach (var field in fields) values.Add(field.Key, field.Value);
        return WireValue.Object(string.Empty, values);
    }
    private static ScriptSource Source(IReadOnlyList<ScriptMessageSchema>? schemas = null) => new(Pack, "mods/fixture/start.lua", "",
        Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant(), 0, schemas, schemas == null ? null : Hash);
    private static List<ScriptMessageSchema> Schemas() => new()
    {
        new() { Name = "Inbound", Direction = AnomalyMessageDirection.ServerToClient,
            Fields = new() { new() { Name = "Value", Type = "Int32" } } },
        new() { Name = "Outbound", Direction = AnomalyMessageDirection.ClientToServer,
            Fields = new() { new() { Name = "Value", Type = "Int32" } } }
    };
    private static ScriptToClientMessage Inbound(int value) => ScriptToClientMessage.Create(Pack, "Inbound",
        ScriptMessagePayload.FromValues(new Dictionary<string, object> { ["Value"] = value })).Bind(Hash, 7);
    private static ScriptSession[] LocalSessions(ScriptRuntime runtime) => ((List<ScriptSession>)typeof(ScriptRuntime)
        .GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime)!).ToArray();
    private static void PublishReady(string scene) => typeof(ClientEvents).GetMethod("OnReady", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, new object[] { new ClientReadyEventArgs(scene) });

    [ScriptType("DevelopmentOnly")]
    private sealed class Fixture
    {
        [ScriptVisible] public int MutatingOperation() => 1;
    }
}
