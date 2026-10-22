using System.Collections.Generic;
using System.Linq;
using Anomaly.Server.Poses;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Poses;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class PoseCrowdBudgetTests
{
    [Theory]
    [InlineData(20, 576)]
    [InlineData(20, 1000)]
    [InlineData(20, 1400)]
    [InlineData(40, 576)]
    [InlineData(40, 1000)]
    [InlineData(40, 1400)]
    public void MaximumBoneCrowdsReceiveFairCompleteSamplesWithinBudgetsAlongsideOtherMessages(int players, int capacity)
    {
        var budget = new PoseTrafficBudget();
        var receiverLimiter = new AnomalyInboundRateLimiter();
        var targets = Enumerable.Range(1, players).Select(i => new PoseTargetState {
            Delivery = new PoseBinding { NetId = (uint)i, ModelGeneration = (ulong)i, BindingRevision = 1,
                RoleId = 1, RigId = "anomaly.classd", SchemaVersion = 1 }, OverrideGeneration = 1 }).ToArray();
        var frames = new PoseFrame("anomaly.classd", 1, Enumerable.Range(0, PoseFrame.MaxBones).Select(i =>
            new PoseBone((ushort)i, PoseComponents.Position | PoseComponents.Rotation, 1, 2, 3, 0, 0, 0, 1)));
        int[] deliveries = new int[players];
        var charges = new List<(double Time, int Packets, int Bytes)>();
        int cursor = 0;
        // Two simulated seconds remain below the real-clock inbound limiter's initial burst.
        // Rolling-window assertions independently prove each production rate ceiling.
        for (int tick = 0; tick < 40; tick++)
        {
            double now = tick * .05d;
            Assert.True(receiverLimiter.TryAccept(0x222, 16 * 1024).Allowed); // unrelated file/control activity at 20 Hz
            if (tick % 10 == 0)
            {
                Assert.True(budget.TryCharge(now, 1, 512));
                Assert.True(receiverLimiter.TryAccept(0x333, 384).Allowed);
                charges.Add((now, 1, 512));
            }
            var offers = new List<PoseSamplePacketizer.Offer>();
            for (int offset = 0; offset < players; offset++)
            {
                int index = (cursor + offset) % players;
                var target = targets[index];
                offers.Add(new PoseSamplePacketizer.Offer { Target = target, Sequence = (ulong)tick + 1,
                    NextCursor = (index + 1) % players,
                    Chunks = PoseSamplePacketizer.Chunk(target.Delivery, 1, (ulong)tick + 1, now, frames, capacity) });
            }
            if (PoseSamplePacketizer.TrySelect(1, offers, capacity, budget, now, out var selected))
            {
                Assert.InRange(selected.Packets.Count, 1, 12);
                foreach (var packet in selected.Packets)
                {
                    int bytes = PoseWireCodec.MessageHeaderBytes + packet.Chunks.Sum(PoseWireCodec.GetSampleChunkSize);
                    Assert.InRange(bytes, 1, capacity);
                    Assert.True(receiverLimiter.TryAccept(0x111, bytes).Allowed);
                    charges.Add((now, 1, bytes + 128));
                }
                foreach (var offer in selected.Offers)
                {
                    deliveries[offer.Target.Delivery.NetId - 1]++;
                    Assert.Equal(PoseFrame.MaxBones, selected.Packets.SelectMany(packet => packet.Chunks)
                        .Where(chunk => chunk.NetId == offer.Target.Delivery.NetId).Sum(chunk => chunk.Bones.Count));
                }
                cursor = selected.Offers.Last().NextCursor;
            }
            var window = charges.Where(charge => now - charge.Time < 1d).ToArray();
            Assert.InRange(window.Sum(charge => charge.Packets), 0, PoseTrafficBudget.MaximumPacketsPerSecond);
            Assert.InRange(window.Sum(charge => charge.Bytes), 0, PoseTrafficBudget.MaximumBytesPerSecond);
            // 20 unrelated messages/second plus bounded pose output remains below aggregate admission.
            Assert.True(window.Sum(charge => charge.Packets) + 20 < AnomalyInboundRateLimiter.AggregateMessagesPerSecond);
        }
        Assert.All(deliveries, count => Assert.True(count > 0));
        Assert.InRange(deliveries.Max() - deliveries.Min(), 0, 1);
    }
}
