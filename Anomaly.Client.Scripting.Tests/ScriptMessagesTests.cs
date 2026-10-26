using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Messaging;
using Anomaly.Client.Scripting.Tests.EventFixtures.Valid;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using MoonSharp.Interpreter;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ScriptMessagesTests
{
    private const string Pack = "fixture:panel";
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public void A_named_message_is_queued_and_replied_to_with_an_injected_pack_and_activation()
    {
        var sent = new List<ScriptToServerMessage>();
        using var session = Session(message => { sent.Add(message); return true; });
        Assert.True(session.Load("seen=0; Messages.On('GetState',function(Message) seen=Message.RequestId; " +
            "Messages.Send('State',{RequestId=Message.RequestId,Enabled=true}) end); function Read() return seen end").Succeeded);
        Assert.True(session.ReceiveMessage(Query(4294967295)));
        Assert.Equal(0d, session.Invoke("Read").Value);
        Assert.Empty(sent);
        Assert.True(session.DispatchQueuedEvent(DispatchTimeout));
        Assert.Equal(4294967295d, session.Invoke("Read").Value);
        var reply = Assert.Single(sent);
        Assert.Equal(Pack, reply.PackId);
        Assert.Equal(Hash, reply.IndexSha256);
        Assert.Equal(7UL, reply.ActivationId);
        Assert.Equal(4294967295u, reply.Payload.Values["RequestId"]);
        Assert.Equal(true, reply.Payload.Values["Enabled"]);
    }

    [Fact]
    public void MessagesRemainReliableAndOrderedRatherThanLatestValueCoalesced()
    {
        using var session = Session();
        Assert.True(session.Load("seen=''; Messages.On('GetState',function(message) seen=seen..message.RequestId..',' end); " +
            "function Read() return seen end").Succeeded);
        for (uint index = 1; index <= 8; index++) Assert.True(session.ReceiveMessage(Query(index)));
        Assert.Equal(8, session.QueuedCount);
        Assert.Equal("", session.Invoke("Read").Value);
        for (var index = 0; index < 8; index++) Assert.True(session.DispatchQueuedEvent(DispatchTimeout));
        Assert.False(session.IsRetired);
        Assert.Equal("1,2,3,4,5,6,7,8,", session.Invoke("Read").Value);
        Assert.Equal(0, session.QueuedMessageBytes);
    }

    [Theory]
    [InlineData("pack")]
    [InlineData("index")]
    [InlineData("activation")]
    [InlineData("unknown")]
    [InlineData("direction")]
    [InlineData("payload")]
    public void Wrong_scope_contract_direction_and_payload_are_rejected_before_queuing(string kind)
    {
        using var session = Session();
        Assert.True(session.Load("Messages.On('GetState',function() error('must not execute') end)").Succeeded);
        var pack = kind == "pack" ? "other:panel" : Pack;
        var name = kind == "unknown" ? "Unknown" : kind == "direction" ? "State" : "GetState";
        var payload = kind == "payload" ? Payload(("RequestId", -1)) : Payload(("RequestId", 1u));
        var message = ScriptToClientMessage.Create(pack, name, payload)
            .Bind(kind == "index" ? new string('b', 64) : Hash, kind == "activation" ? 8UL : 7UL);
        Assert.False(session.ReceiveMessage(message));
        Assert.Equal(0, session.QueuedCount);
        Assert.Equal(0, session.QueuedMessageBytes);
        Assert.False(session.IsRetired);
    }

    [Fact]
    public void Known_messages_without_handlers_are_safely_dropped()
    {
        using var session = Session();
        Assert.True(session.Load("").Succeeded);
        Assert.True(session.ReceiveMessage(Query(1)));
        Assert.Equal(0, session.QueuedCount);
    }

    [Fact]
    public void Installing_messages_does_not_retain_the_mutable_producer_contract_graph()
    {
        var (session, producer) = SessionWithCollectibleContracts();
        using (session)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(producer.IsAlive);
            Assert.True(session.Load("Messages.On('GetState',function() end)").Succeeded);
            Assert.True(session.ReceiveMessage(Query(1)));
            GC.KeepAlive(session);
        }
    }

    [Fact]
    public void Message_method_lookups_reuse_a_fixed_set_of_accounted_clr_wrappers()
    {
        using var session = Session();
        var script = Vm(session);
        var capability = script.Globals.Get("Messages").UserData;
        var names = new[] { "On", "Off", "Send" }.Select(DynValue.NewString).ToArray();
        using var scope = script.MemoryBudget.EnterScope();
        var operations = names.Select(name => capability.Descriptor.Index(script, capability.Object, name, true)).ToArray();
        var before = session.MemoryTotalAllocatedBytes;
        for (var pass = 0; pass < 1000; pass++)
            for (var index = 0; index < names.Length; index++)
                Assert.Same(operations[index], capability.Descriptor.Index(script, capability.Object, names[index], true));
        Assert.Equal(before, session.MemoryTotalAllocatedBytes);
        Assert.Equal(0, session.MemoryUnboundBytes);
    }

    [Fact]
    public void Maximum_field_contract_authorization_allocates_no_per_message_scratch_collections()
    {
        var schema = new ScriptMessageSchema { Name = "Full", Direction = AnomalyMessageDirection.ServerToClient };
        var values = new Dictionary<string, object>();
        for (var index = 0; index < ScriptMessageLimits.MaxFields; index++)
        {
            var name = "Field" + index;
            schema.Fields.Add(new ScriptMessageField { Name = name, Type = "UInt32" });
            values.Add(name, (uint)index);
        }
        var payload = ScriptMessagePayload.FromValues(values);
        for (var index = 0; index < 16; index++) schema.ValidatePayload(payload);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++) schema.ValidatePayload(payload);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        schema.Fields[^1].Name = schema.Fields[0].Name;
        Assert.Throws<InvalidDataException>(schema.Validate);
    }

    [Fact]
    public void Oversized_context_inventory_is_rejected_before_session_dictionary_allocation()
    {
        var script = new Script(CoreModules.None) { MemoryBudget = new MemoryBudget(1, reclaimable: true) };
        var contracts = Enumerable.Range(0, ScriptMessageLimits.MaxSchemasPerPack + 1)
            .Select(_ => Schemas()[0]).ToArray();
        var context = new ScriptMessageContext(Pack, Hash, 7, contracts, () => true, _ => true);
        Assert.Throws<ArgumentException>(() => new ScriptMessages(context, script, (_, _) => true, new ScriptSubscriptionBudget()));
        Assert.Equal(0, script.MemoryBudget.TotalAllocatedBytes);
        Assert.False(script.MemoryBudget.IsTerminated);
        Assert.Equal(0, script.MemoryBudget.UnboundBytes);
    }

    [Theory]
    [InlineData("Messages.On('Unknown',function() end)")]
    [InlineData("Messages.On('State',function() end)")]
    [InlineData("Messages.On('GetState',1)")]
    [InlineData("Messages.GetType()")]
    [InlineData("Messages.Send={}")]
    [InlineData("local fake={On=Messages.On}; fake:On('GetState',function() end)")]
    [InlineData("local f=Messages.On; f(Messages,'GetState',function() end)")]
    public void Invalid_registration_or_capability_receivers_retire_the_vm(string source)
    {
        using var session = Session();
        Assert.Equal(ScriptExecutionStatus.ScriptError, session.Load(source).Status);
        Assert.True(session.IsRetired);
    }

    [Theory]
    [InlineData("{RequestId=-1,Enabled=true}")]
    [InlineData("{RequestId=4294967296,Enabled=true}")]
    [InlineData("{RequestId=0.5,Enabled=true}")]
    [InlineData("{RequestId=0/0,Enabled=true}")]
    [InlineData("{RequestId=1/0,Enabled=true}")]
    [InlineData("{RequestId='1',Enabled=true}")]
    [InlineData("{RequestId={},Enabled=true}")]
    [InlineData("{RequestId=function() end,Enabled=true}")]
    [InlineData("{RequestId=1,Enabled=1}")]
    [InlineData("{RequestId=1}")]
    [InlineData("{RequestId=1,Enabled=true,Extra=1}")]
    [InlineData("{[1]=1,Enabled=true}")]
    [InlineData("nil")]
    public void Invalid_outbound_records_are_returned_as_bounded_errors_without_sending(string record)
    {
        var sends = 0;
        using var session = Session(_ => { sends++; return true; });
        var result = session.Load("local Ok,Error=Messages.Send('State'," + record + "); " +
            "assert(Ok==false and Error=='InvalidPayload')");
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(0, sends);
        Assert.False(session.IsRetired);
    }

    [Fact]
    public void Cyclic_and_metatable_payloads_are_not_traversed_and_no_metamethod_runs()
    {
        var sends = 0;
        using var session = Session(_ => { sends++; return true; });
        var script = Vm(session);
        var table = new Table(script);
        var metatable = new Table(script);
        var calls = 0;
        metatable.Set("__index", DynValue.NewCallback((_, _) => { calls++; return DynValue.NewNumber(1); }));
        table.MetaTable = metatable;
        script.Globals.Set("Input", DynValue.NewTable(table));
        Assert.True(session.Load("local Cycle={}; Cycle.RequestId=Cycle; Cycle.Enabled=true; " +
            "local Ok,Error=Messages.Send('State',Cycle); assert(not Ok and Error=='InvalidPayload'); " +
            "Ok,Error=Messages.Send('State',Input); assert(not Ok and Error=='InvalidPayload')").Succeeded);
        Assert.Equal(0, calls);
        Assert.Equal(0, sends);
    }

    [Fact]
    public void Dot_and_colon_operations_work_and_off_invalidates_already_queued_generations()
    {
        using var session = Session();
        Assert.True(session.Load("seen=0; local Callback=function(Message) seen=Message.RequestId end; " +
            "local Token=Messages:On('GetState',Callback); " +
            "function Replace() assert(Messages:Off(Token)); assert(not Messages.Off(Token)); " +
            "Token=Messages.On('GetState',Callback) end; function Read() return seen end").Succeeded);
        Assert.True(session.ReceiveMessage(Query(1)));
        Assert.True(session.Invoke("Replace").Succeeded);
        Assert.True(session.DispatchQueuedEvent(DispatchTimeout));
        Assert.Equal(0d, session.Invoke("Read").Value);
        Assert.True(session.ReceiveMessage(Query(2)));
        Assert.True(session.DispatchQueuedEvent(DispatchTimeout));
        Assert.Equal(2d, session.Invoke("Read").Value);
    }

    [Fact]
    public void Callback_and_capability_values_cannot_be_reused_by_another_vm()
    {
        using var first = Session();
        using var second = Session();
        Assert.True(first.Load("function Callback() end").Succeeded);
        Assert.Throws<ScriptRuntimeException>(() => Vm(second).Globals.Set("Foreign", Vm(first).Globals.Get("Callback")));
        using var third = Session();
        Vm(third).Globals.Set("Foreign", Vm(first).Globals.Get("Messages"));
        Assert.Equal(ScriptExecutionStatus.ScriptError, third.Load("Foreign.On('GetState',function() end)").Status);
    }

    [Fact]
    public void Event_and_message_subscriptions_share_one_limit_and_off_returns_capacity()
    {
        SignalsEvents.Reset();
        var bindings = new ScriptBindingRegistry();
        using var catalog = EventDiscoveryTests.Discover(bindings);
        using var session = Session(eventCatalog: catalog, bindings: bindings);
        Assert.True(session.Load("local Tokens={}; for i=1,31 do Tokens[i]=Messages.On('GetState',function() end) end; " +
            "Events.Signals.Changed.Add(function() end); assert(Messages.Off(Tokens[1])); " +
            "Tokens[1]=Messages.On('GetState',function() end); " +
            "function Overflow() Messages.On('GetState',function() end) end").Succeeded);
        Assert.Equal(ScriptExecutionStatus.ScriptError, session.Invoke("Overflow").Status);
        Assert.True(session.IsRetired);
        Assert.Equal(1, SignalsEvents.Removes);
    }

    [Fact]
    public void Message_and_event_deliveries_share_the_same_64_queue_slots()
    {
        using var session = Session();
        Assert.True(session.Load("Messages.On('GetState',function() end)").Succeeded);
        for (var index = 0; index < 63; index++) Assert.True(session.QueueEvent("Optional"));
        Assert.True(session.ReceiveMessage(Query(1)));
        Assert.Equal(64, session.QueuedCount);
        Assert.False(session.ReceiveMessage(Query(2)));
        Assert.True(session.IsRetired);
        Assert.Equal(0, session.QueuedCount);
        Assert.Equal(0, session.QueuedMessageBytes);
    }

    [Fact]
    public void Queued_payload_bytes_are_bounded_and_reclaimed_when_deliveries_are_dispatched()
    {
        var schema = TextSchema();
        using var session = Session(schemas: new[] { schema });
        Assert.True(session.Load("Messages.On('Text',function() return {} end)").Succeeded);
        var payload = Payload(("A", new string('a', 980)), ("B", new string('b', 980)),
            ("C", new string('c', 980)), ("D", new string('d', 980)));
        var message = ScriptToClientMessage.Create(Pack, "Text", payload).Bind(Hash, 7);
        for (var pass = 0; pass < 2; pass++)
        {
            for (var index = 0; index < 16; index++) Assert.True(session.ReceiveMessage(message));
            Assert.Equal(16 * payload.EncodedBytes, session.QueuedMessageBytes);
            for (var index = 0; index < 16; index++) Assert.True(session.DispatchQueuedEvent(DispatchTimeout));
            Assert.Equal(0, session.QueuedMessageBytes);
        }
        for (var index = 0; index < 16; index++) Assert.True(session.ReceiveMessage(message));
        Assert.False(session.ReceiveMessage(message));
        Assert.True(session.IsRetired);
    }

    [Theory]
    [InlineData("for i=1,100000 do end", "CpuLimit")]
    [InlineData("local Retained=string.rep('x',1048576)", "MemoryLimit")]
    public void Message_callbacks_use_the_same_cpu_and_memory_execution_boundary(string body, string expected)
    {
        string? diagnostic = null;
        // A single oversized reservation isolates memory containment from the
        // independent callback deadline; an allocation loop races both limits.
        var options = new ScriptExecutionOptions { Timeout = DispatchTimeout,
            InstructionLimit = expected == "CpuLimit" ? 2000 : 2_000_000,
            AllocationLimitBytes = expected == "MemoryLimit" ? 2 * 1024 * 1024 : 16 * 1024 * 1024 };
        using var session = Session(options: options, diagnostic: message => diagnostic = message);
        Assert.True(session.Load("Messages.On('GetState',function() " + body + " end)").Succeeded);
        Assert.True(session.ReceiveMessage(Query(1)));
        Assert.True(session.DispatchQueuedEvent(DispatchTimeout));
        Assert.True(session.IsRetired);
        Assert.NotNull(diagnostic);
        Assert.Contains(expected, diagnostic);
    }

    [Fact]
    public void A_stale_connection_invalidates_queued_callbacks_and_blocks_sends()
    {
        var current = true;
        var sends = 0;
        using var session = Session(_ => { sends++; return true; }, isCurrent: () => current);
        Assert.True(session.Load("seen=0; Messages.On('GetState',function() seen=seen+1 end); function Read() return seen end; " +
            "function Send() local Ok,Error=Messages.Send('State',{RequestId=1,Enabled=true}); return Error end").Succeeded);
        Assert.True(session.ReceiveMessage(Query(1)));
        current = false;
        Assert.True(session.DispatchQueuedEvent(DispatchTimeout));
        Assert.Equal(0d, session.Invoke("Read").Value);
        Assert.Equal("NotReady", session.Invoke("Send").Value);
        Assert.False(session.ReceiveMessage(Query(2)));
        Assert.Equal(0, sends);
    }

    [Fact]
    public void Local_pre_activation_handlers_are_allowed_but_send_needs_a_real_activation_grant()
    {
        using var session = Session(activation: 0);
        Assert.True(session.Load("Messages.On('GetState',function() end); local Ok,Error=" +
            "Messages.Send('State',{RequestId=1,Enabled=true}); assert(not Ok and Error=='NotReady')").Succeeded);
        Assert.False(session.ReceiveMessage(Query(1)));
    }

    [Theory]
    [InlineData("{Signed=-2147483648,Number=1.25,Text='éé'}", true)]
    [InlineData("{Signed=2147483647,Number=-1.25,Text=''}", true)]
    [InlineData("{Signed=2147483648,Number=1,Text='x'}", false)]
    [InlineData("{Signed=-2147483649,Number=1,Text='x'}", false)]
    [InlineData("{Signed=1.5,Number=1,Text='x'}", false)]
    [InlineData("{Signed=1,Number=0/0,Text='x'}", false)]
    [InlineData("{Signed=1,Number=1/0,Text='x'}", false)]
    [InlineData("{Signed=1,Number=1,Text='ééé'}", false)]
    [InlineData("{Signed=1,Number=1,Text='a\\0b'}", false)]
    public void Signed_numbers_finite_numbers_and_utf8_strings_follow_exact_schema_bounds(string record, bool valid)
    {
        var schema = new ScriptMessageSchema { Name = "Write", Direction = AnomalyMessageDirection.ClientToServer,
            Fields = new() { new() { Name = "Signed", Type = "Int32" }, new() { Name = "Number", Type = "Number" },
                new() { Name = "Text", Type = "String", MaxBytes = 4 } } };
        var sends = 0;
        using var session = Session(_ => { sends++; return true; }, schemas: new[] { schema });
        var result = session.Load("local Ok,Error=Messages.Send('Write'," + record + "); assert(Ok==" +
            (valid ? "true and Error==nil" : "false and Error=='InvalidPayload'") + ")");
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(valid ? 1 : 0, sends);
    }

    [Fact]
    public void Individually_bounded_strings_cannot_exceed_the_total_encoded_record_limit()
    {
        var schema = TextSchema();
        schema.Direction = AnomalyMessageDirection.ClientToServer;
        schema.Fields.Add(new ScriptMessageField { Name = "E", Type = "String", MaxBytes = 980 });
        var sends = 0;
        using var session = Session(_ => { sends++; return true; }, schemas: new[] { schema });
        Assert.True(session.Load("local Text=string.rep('a',980); local Ok,Error=Messages.Send('Text'," +
            "{A=Text,B=Text,C=Text,D=Text,E=Text}); assert(not Ok and Error=='InvalidPayload')").Succeeded);
        Assert.Equal(0, sends);
    }

    [Fact]
    public void A_required_callback_fault_stops_other_vms_in_the_same_runtime_tick()
    {
        using var runtime = new ScriptRuntime();
        using var set = new ConnectionScriptSet(runtime, () => true, activationId: 7);
        var first = Source("Messages.On('GetState',function() error('required fault') end)", Schemas());
        var second = Source("seen=0; Messages.On('GetState',function() seen=seen+1 end); function Read() return seen end", Schemas())
            with { Address = "mods/fixture/z.lua" };
        Assert.True(set.InitializeLocal(new[] { first, second }, true, out var error), error);
        var sessions = ((List<ScriptSession>)typeof(ScriptRuntime).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runtime)!).ToArray();
        Assert.True(set.Receive(Query(1)));
        runtime.Tick(() => set.IsHealthy);
        Assert.False(set.IsHealthy);
        Assert.True(sessions[0].IsRetired);
        Assert.False(sessions[1].IsRetired);
        Assert.Equal(0d, sessions[1].Invoke("Read").Value);
        Assert.Equal(1, sessions[1].QueuedCount);
    }

    [Fact]
    public void Synchronous_transport_revocation_during_a_send_clears_every_pending_callback()
    {
        using var runtime = new ScriptRuntime();
        var current = true;
        var sends = 0;
        using var set = new ConnectionScriptSet(runtime, () => current, _ =>
        {
            sends++;
            current = false;
            runtime.RevokeAll();
            return false;
        }, 7);
        var first = Source("Messages.On('GetState',function() Messages.Send('State',{RequestId=1,Enabled=true}) end)", Schemas());
        var second = first with { Address = "mods/fixture/z.lua" };
        Assert.True(set.InitializeLocal(new[] { first, second }, true, out var error), error);
        var sessions = ((List<ScriptSession>)typeof(ScriptRuntime).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runtime)!).ToArray();
        Assert.True(set.Receive(Query(1)));
        runtime.Tick(() => set.IsHealthy);
        Assert.Equal(1, sends);
        Assert.False(set.IsHealthy);
        Assert.All(sessions, session => { Assert.True(session.IsRetired); Assert.Equal(0, session.QueuedCount); });
        Assert.False(set.Receive(Query(2)));
    }

    [Fact]
    public void The_runtime_catalog_describes_messages_without_making_them_development_globals()
    {
        using var runtime = new ScriptRuntime();
        var catalog = runtime.GetCatalog();
        Assert.True(catalog.HasMessagesGlobal);
        Assert.Contains("global Messages: pack-message-registry", catalog.DescribeSurface());
        Assert.Contains("function Messages.On(Name, Callback)", catalog.ToLuaDefinitions());
        Assert.Contains("function Messages.Off(SubscriptionToken)", catalog.ToLuaDefinitions());
        Assert.Contains("function Messages.Send(Name, Payload)", catalog.ToLuaDefinitions());
        Assert.Contains("pack-message-registry", catalog.ToJson());
        new Script(CoreModules.None).LoadString(catalog.ToLuaDefinitions());
        using var session = runtime.CreateDevelopmentSession();
        Assert.True(session.Load("assert(Messages==nil)").Succeeded);
        Assert.Throws<ArgumentException>(() => session.SetGlobal("Messages", null));
    }

    [Fact]
    public void Approved_sources_clone_contracts_before_execution_and_scope_to_the_matching_pack()
    {
        using var runtime = new ScriptRuntime();
        using var set = new ConnectionScriptSet(runtime, () => true, activationId: 7);
        var schemas = Schemas();
        var source = Source("Messages.On('GetState',function() end)", schemas);
        Assert.True(set.InitializeLocal(new[] { source }, true, out var error), error);
        schemas[0].Fields.Clear();
        schemas[0].Name = "Mutated";
        Assert.True(set.Receive(Query(1)));
        Assert.False(set.Receive(ScriptToClientMessage.Create("other:panel", "GetState", Payload(("RequestId", 1u))).Bind(Hash, 7)));
        set.Dispose();
        Assert.False(set.Receive(Query(1)));
    }

    [Theory]
    [InlineData("index")]
    [InlineData("contract")]
    public void Same_pack_scripts_cannot_smuggle_different_contracts_into_one_set(string kind)
    {
        using var runtime = new ScriptRuntime();
        using var set = new ConnectionScriptSet(runtime, () => true, activationId: 7);
        var first = Source("", Schemas());
        var second = Source("", Schemas()) with { Address = "mods/fixture/second.lua" };
        if (kind == "index") second = second with { IndexSha256 = new string('b', 64) };
        else second.MessageSchemas![0].Fields[0].Type = "Int32";
        Assert.False(set.InitializeLocal(new[] { first, second }, true, out _));
        Assert.False(set.IsHealthy);
        Assert.False(set.Receive(Query(1)));
    }

    [Theory]
    [InlineData("UnknownMessage", "Unknown")]
    [InlineData("WrongDirection", "GetState")]
    public void Sends_fail_with_a_specific_error_for_unknown_or_wrong_direction_contracts(string error, string name)
    {
        using var session = Session();
        Assert.True(session.Load("local Ok,Error=Messages.Send('" + name + "',{}); assert(not Ok and Error=='" + error + "')").Succeeded);
    }

    private static ScriptSession Session(Func<ScriptToServerMessage, bool>? send = null,
        Func<bool>? isCurrent = null, IReadOnlyList<ScriptMessageSchema>? schemas = null,
        ScriptExecutionOptions? options = null, Action<string>? diagnostic = null, ulong activation = 7,
        Anomaly.Client.Scripting.Events.ScriptEventCatalog? eventCatalog = null, ScriptBindingRegistry? bindings = null)
    {
        var session = new ScriptSession(bindings ?? new ScriptBindingRegistry(), options ??
            new ScriptExecutionOptions { Timeout = DispatchTimeout }, diagnostic, eventCatalog);
        session.InstallMessages(new ScriptMessageContext(Pack, Hash, activation, schemas ?? Schemas(),
            isCurrent ?? (() => true), send ?? (_ => true)));
        return session;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ScriptSession Session, WeakReference Producer) SessionWithCollectibleContracts()
    {
        var schemas = Schemas();
        return (Session(schemas: schemas), new WeakReference(schemas));
    }

    private static List<ScriptMessageSchema> Schemas() => new()
    {
        new() { Name = "GetState", Direction = AnomalyMessageDirection.ServerToClient,
            Fields = new() { new() { Name = "RequestId", Type = "UInt32" } } },
        new() { Name = "State", Direction = AnomalyMessageDirection.ClientToServer,
            Fields = new() { new() { Name = "RequestId", Type = "UInt32" }, new() { Name = "Enabled", Type = "Boolean" } } }
    };
    private static ScriptMessageSchema TextSchema() => new()
    {
        Name = "Text", Direction = AnomalyMessageDirection.ServerToClient,
        Fields = new() { new() { Name = "A", Type = "String", MaxBytes = 980 }, new() { Name = "B", Type = "String", MaxBytes = 980 },
            new() { Name = "C", Type = "String", MaxBytes = 980 }, new() { Name = "D", Type = "String", MaxBytes = 980 } }
    };
    private static ScriptMessagePayload Payload(params (string Name, object Value)[] values) =>
        ScriptMessagePayload.FromValues(values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal));
    private static ScriptToClientMessage Query(uint requestId) =>
        ScriptToClientMessage.Create(Pack, "GetState", Payload(("RequestId", requestId))).Bind(Hash, 7);
    private static Script Vm(ScriptSession session) => (Script)typeof(ScriptSession)
        .GetField("_script", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
    private static ScriptSource Source(string source, IReadOnlyList<ScriptMessageSchema> schemas)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return new ScriptSource(Pack, "mods/fixture/start.lua", source,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length, schemas, Hash);
    }
}
