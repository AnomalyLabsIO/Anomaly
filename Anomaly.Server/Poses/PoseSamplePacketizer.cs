using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Poses;

namespace Anomaly.Server.Poses;

/// <summary>Splits complete model samples and batches chunks without relying on transport fragmentation.</summary>
internal static class PoseSamplePacketizer
{
    internal sealed class Offer
    {
        internal PoseTargetState Target;
        internal ulong Sequence;
        internal List<PoseSampleChunk> Chunks;
        internal int NextCursor;
    }
    internal sealed class Selection
    {
        internal List<Offer> Offers;
        internal List<PlayerPoseSampleMessage> Packets;
    }

    /// <summary>Selects a fair ordered prefix and reserves whole samples before any fragment is sent.</summary>
    internal static bool TrySelect(uint epoch, IEnumerable<Offer> offers, int maximumPayload,
        PoseTrafficBudget budget, double now, out Selection selection, bool combat = false)
    {
        selection = null;
        var selected = new List<Offer>();
        foreach (Offer offer in offers)
        {
            selected.Add(offer);
            if (Pack(epoch, selected.SelectMany(work => work.Chunks), maximumPayload).Count <= (combat ? 8 : 12)) continue;
            selected.RemoveAt(selected.Count - 1);
            break;
        }
        while (selected.Count > 0)
        {
            var packets = Pack(epoch, selected.SelectMany(work => work.Chunks), maximumPayload);
            int bytes = packets.Sum(packet => PoseWireCodec.MessageHeaderBytes + packet.Chunks.Sum(PoseWireCodec.GetSampleChunkSize) + 128);
            if (!budget.TryChargeSamples(now, packets.Count, bytes, combat)) { selected.RemoveAt(selected.Count - 1); continue; }
            selection = new Selection { Offers = selected, Packets = packets };
            return true;
        }
        return false;
    }

    internal static List<PoseSampleChunk> Chunk(PoseBinding binding, ulong generation, ulong sequence,
        double time, PoseFrame frame, int maximumPayload)
    {
        int maximum = maximumPayload - PoseWireCodec.MessageHeaderBytes;
        if (maximum < PoseWireCodec.SampleChunkHeaderBytes + 17) return new List<PoseSampleChunk>();
        var chunks = new List<PoseSampleChunk>();
        PoseSampleChunk current = null;
        int size = 0;
        foreach (PoseBone bone in frame.Bones)
        {
            int boneSize = PoseWireCodec.GetBoneSize(bone);
            if (current == null || size + boneSize > maximum)
            {
                current = new PoseSampleChunk { NetId = binding.NetId, ModelGeneration = binding.ModelGeneration,
                    BindingRevision = binding.BindingRevision, OverrideGeneration = generation,
                    Sequence = sequence, ServerTime = time };
                chunks.Add(current); size = PoseWireCodec.SampleChunkHeaderBytes;
            }
            current.Bones.Add(bone); size += boneSize;
        }
        if (chunks.Count > PoseSampleChunk.MaxChunks) return new List<PoseSampleChunk>();
        for (int i = 0; i < chunks.Count; i++) { chunks[i].ChunkIndex = (byte)i; chunks[i].ChunkCount = (byte)chunks.Count; }
        return chunks;
    }

    internal static List<PlayerPoseSampleMessage> Pack(uint epoch, IEnumerable<PoseSampleChunk> chunks, int maximumPayload)
    {
        var packets = new List<PlayerPoseSampleMessage>();
        PlayerPoseSampleMessage packet = null;
        int length = 0;
        foreach (PoseSampleChunk chunk in chunks)
        {
            int size = PoseWireCodec.GetSampleChunkSize(chunk);
            if (size + PoseWireCodec.MessageHeaderBytes > maximumPayload)
                throw new ArgumentException("A pose chunk exceeds recipient capacity.", nameof(chunks));
            if (packet == null || length + size > maximumPayload || packet.Chunks.Count >= PlayerPoseSampleMessage.MaxChunks)
            {
                packet = new PlayerPoseSampleMessage { RoundEpoch = epoch };
                packets.Add(packet); length = PoseWireCodec.MessageHeaderBytes;
            }
            packet.Chunks.Add(chunk); length += size;
        }
        return packets;
    }
}
