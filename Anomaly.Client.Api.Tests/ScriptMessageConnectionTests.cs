using Anomaly.Client.Internal.Networking;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class ScriptMessageConnectionTests
{
    private const string PackId = "example:pack";
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static ScriptMessagePayload Data(object value) => ScriptMessagePayload.FromValues(
        new Dictionary<string, object> { ["Enabled"] = value });
    private static ScriptMessageSchema Schema(string name, AnomalyMessageDirection direction) => new()
    {
        Name = name, Direction = direction,
        Fields = new List<ScriptMessageField> { new() { Name = "Enabled", Type = "Boolean" } }
    };
    private static ScriptMessageConnection Scope(Func<bool>? current = null, Func<bool>? ready = null,
        ScriptMessageSchema? schema = null, Func<double>? clock = null) => new(7,
        new[] { (PackId, Hash, (IReadOnlyList<ScriptMessageSchema>)new[]
        { schema ?? Schema("State", AnomalyMessageDirection.ServerToClient), Schema("Choose", AnomalyMessageDirection.ClientToServer) }) },
        current ?? (() => true), ready ?? (() => true), clock!);
    private static ScriptToClientMessage Incoming(string pack = PackId, string hash = Hash, ulong activation = 7, string name = "State") =>
        ScriptToClientMessage.Create(pack, name, Data(true)).Bind(hash, activation);

    [Fact]
    public void Valid_scope_allows_both_directions_with_separate_budgets()
    {
        var scope = Scope(clock: () => 1);
        for (int index = 0; index < 10; index++)
        {
            Assert.True(scope.TryReceive(Incoming()));
            Assert.True(scope.TrySend(ScriptToServerMessage.Create(PackId, "Choose", Data(false)).Bind(Hash, 7)));
        }
        Assert.False(scope.TryReceive(Incoming()));
        Assert.False(scope.TrySend(ScriptToServerMessage.Create(PackId, "Choose", Data(false)).Bind(Hash, 7)));
    }

    [Theory]
    [InlineData("pack")]
    [InlineData("hash")]
    [InlineData("activation")]
    [InlineData("name")]
    [InlineData("direction")]
    [InlineData("payload")]
    public void Wrong_pack_scope_direction_or_payload_cannot_enter_delivery(string kind)
    {
        var scope = Scope();
        var message = kind switch
        {
            "pack" => Incoming(pack: "other:pack"),
            "hash" => Incoming(hash: new string('b', 64)),
            "activation" => Incoming(activation: 8),
            "name" => Incoming(name: "Unknown"),
            "direction" => Incoming(name: "Choose"),
            "payload" => ScriptToClientMessage.Create(PackId, "State", Data(1)).Bind(Hash, 7),
            _ => throw new InvalidOperationException()
        };
        Assert.False(scope.TryReceive(message));
    }

    [Fact]
    public void Revocation_and_readiness_are_checked_on_every_operation()
    {
        bool current = true, ready = false;
        var scope = Scope(() => current, () => ready);
        Assert.False(scope.TryReceive(Incoming()));
        ready = true;
        Assert.True(scope.TryReceive(Incoming()));
        current = false;
        Assert.False(scope.TryReceive(Incoming()));
        Assert.False(scope.TrySend(ScriptToServerMessage.Create(PackId, "Choose", Data(false)).Bind(Hash, 7)));
    }

    [Fact]
    public void Mutating_the_offered_schema_cannot_expand_the_approved_grant()
    {
        var schema = Schema("State", AnomalyMessageDirection.ServerToClient);
        var scope = Scope(schema: schema);
        schema.Fields[0].Type = "Int32";
        schema.Name = "Changed";
        Assert.True(scope.TryReceive(Incoming()));
        Assert.False(scope.TryReceive(ScriptToClientMessage.Create(PackId, "State", Data(1)).Bind(Hash, 7)));
    }
}
