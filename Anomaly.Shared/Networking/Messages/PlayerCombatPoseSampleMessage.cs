using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Poses;

namespace Anomaly.Shared.Networking.Messages;

/// <summary>Unreliable complete-sample fragments. Unknown or obsolete stream identities are harmless drops.</summary>
public sealed class PlayerCombatPoseSampleMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
{
    public const int MaxChunks = 128;
    public override int MaxPayloadBytes => 64 * 1024;
    public string MessageName => "anomaly:combat-pose:sample";
    public MessageChannel TransportChannel => MessageChannel.Unreliable;
    public uint RoundEpoch { get; set; }
    public List<PoseSampleChunk> Chunks { get; set; } = new List<PoseSampleChunk>();
    public void Serialize(BinaryWriter writer)
    {
        if (RoundEpoch == 0 || Chunks == null || Chunks.Count > MaxChunks) throw new InvalidDataException("Invalid pose sample header.");
        writer.Write(RoundEpoch); writer.Write((ushort)Chunks.Count);
        foreach (var chunk in Chunks) PoseWireCodec.WriteChunk(writer, chunk);
    }
    public void Deserialize(BinaryReader reader)
    {
        RoundEpoch = reader.ReadUInt32(); int count = reader.ReadUInt16();
        if (RoundEpoch == 0 || count > MaxChunks) throw new InvalidDataException("Invalid pose sample header.");
        var chunks = new List<PoseSampleChunk>(count);
        for (int i = 0; i < count; i++) chunks.Add(PoseWireCodec.ReadChunk(reader));
        Chunks = chunks;
    }
}
