using System.IO;
using Anomaly.Shared.Scripting;

namespace Anomaly.Shared.Networking.Messages;

/// <summary>A primitive record from one authenticated client's approved pack.</summary>
public sealed class ScriptToServerMessage : AuthenticatedClientToServerMessage, IAnomalyMessage, IScriptMessage
{
    public override int MaxPayloadBytes => ScriptMessageWire.MaxEnvelopeBytes;
    public string MessageName => "anomaly:script:server";
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public string PackId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string IndexSha256 { get; private set; } = string.Empty;
    public ulong ActivationId { get; private set; }
    public ScriptMessagePayload Payload { get; private set; }

    public static ScriptToServerMessage Create<T>(string packId, string name, T data) => Create(packId, name, ScriptMessagePayload.Create(data));
    public static ScriptToServerMessage Create(string packId, string name, ScriptMessagePayload payload)
    {
        ScriptMessageWire.Validate(packId, name, payload);
        return new ScriptToServerMessage { PackId = packId, Name = name, Payload = payload };
    }
    public ScriptToServerMessage Bind(string indexSha256, ulong activationId)
    {
        ScriptMessageWire.ValidateScope(indexSha256, activationId);
        ScriptToServerMessage bound = Create(PackId, Name, Payload);
        bound.IndexSha256 = indexSha256; bound.ActivationId = activationId;
        return bound;
    }
    public bool TryRead<T>(out T data) where T : new()
    {
        if (Payload != null) return Payload.TryRead(out data);
        data = default; return false;
    }
    public void Serialize(BinaryWriter writer) => ScriptMessageWire.Write(writer, this);
    public void Deserialize(BinaryReader reader)
    {
        ScriptMessageWire.Read(reader, out string pack, out string name, out string hash, out ulong activation, out ScriptMessagePayload payload);
        PackId = pack; Name = name; IndexSha256 = hash; ActivationId = activation; Payload = payload;
    }
}
