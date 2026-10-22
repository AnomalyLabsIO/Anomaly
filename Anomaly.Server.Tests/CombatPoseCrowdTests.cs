using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Server.Poses;
using Anomaly.Shared.Poses;
using Xunit;
using Xunit.Abstractions;

namespace Anomaly.Server.Tests;

public sealed class CombatPoseCrowdTests
{
    private readonly ITestOutputHelper _output;
    public CombatPoseCrowdTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(20, 576)]
    [InlineData(20, 918)]
    [InlineData(40, 576)]
    [InlineData(40, 918)]
    public void BothAuthoritiesStayFreshWithFullCosmeticAndLargestStockCombatCrowds(int players, int capacity)
    {
        var budget = new PoseTrafficBudget();
        // 53 full transform slots cover the largest stock visual rig; 23 cover the largest actual hitbox ancestor union (939).
        PoseFrame cosmetic = Frame(53), combat = Frame(23);
        var targets = Enumerable.Range(1, players).Select(i => new PoseTargetState {
            Delivery = new PoseBinding { NetId = (uint)i, ModelGeneration = 1, BindingRevision = 1,
                RoleId = 1, RigId = "anomaly.classd", SchemaVersion = 1 }, OverrideGeneration = 1 }).ToArray();
        int[] cursors = new int[2];
        int[,] received = new int[2, players];
        double[,] last = new double[2, players];
        double[] maximumGap = new double[2];
        var charges = new List<(double Time, int Packets, int Bytes)>();
        int peakBytes = 0, peakPackets = 0;
        for (int tick = 0; tick < 240; tick++)
        {
            double now = tick * .05;
            if (tick % 5 == 0)
            {
                Assert.True(budget.TryCharge(now, 1, 512));
                charges.Add((now, 1, 512));
            }
            // Combat is serviced first; independent reservations must still make the visual stream fair.
            for (int lane = 0; lane < 2; lane++)
            {
                PoseFrame frame = lane == 0 ? combat : cosmetic;
                var offers = new List<PoseSamplePacketizer.Offer>();
                for (int offset = 0; offset < players; offset++)
                {
                    int index = (cursors[lane] + offset) % players;
                    offers.Add(new PoseSamplePacketizer.Offer { Target = targets[index], Sequence = (ulong)tick + 1,
                        NextCursor = (index + 1) % players,
                        Chunks = PoseSamplePacketizer.Chunk(targets[index].Delivery, 1, (ulong)tick + 1, now, frame, capacity) });
                }
                if (!PoseSamplePacketizer.TrySelect(1, offers, capacity, budget, now, out var selected, combat: lane == 0)) continue;
                Assert.InRange(selected.Packets.Count, 1, lane == 0 ? 8 : 12);
                foreach (var packet in selected.Packets)
                {
                    int bytes = PoseWireCodec.MessageHeaderBytes + packet.Chunks.Sum(PoseWireCodec.GetSampleChunkSize);
                    Assert.InRange(bytes, 1, capacity);
                    charges.Add((now, 1, bytes + 128));
                }
                foreach (var offer in selected.Offers)
                {
                    int index = (int)offer.Target.Delivery.NetId - 1;
                    if (received[lane, index] > 0) maximumGap[lane] = Math.Max(maximumGap[lane], now - last[lane, index]);
                    else Assert.True(now < .5, "Every subject receives its first frame before combat expiry.");
                    last[lane, index] = now;
                    received[lane, index]++;
                    Assert.Equal(frame.Bones.Count, selected.Packets.SelectMany(packet => packet.Chunks)
                        .Where(chunk => chunk.NetId == offer.Target.Delivery.NetId).Sum(chunk => chunk.Bones.Count));
                }
                cursors[lane] = selected.Offers.Last().NextCursor;
            }
            var window = charges.Where(charge => now - charge.Time < 1).ToArray();
            int bytesNow = window.Sum(charge => charge.Bytes), packetsNow = window.Sum(charge => charge.Packets);
            peakBytes = Math.Max(peakBytes, bytesNow); peakPackets = Math.Max(peakPackets, packetsNow);
            Assert.InRange(bytesNow, 0, PoseTrafficBudget.MaximumBytesPerSecond);
            Assert.InRange(packetsNow, 0, PoseTrafficBudget.MaximumPacketsPerSecond);
        }
        for (int lane = 0; lane < 2; lane++)
        {
            int[] counts = Enumerable.Range(0, players).Select(i => received[lane, i]).ToArray();
            Assert.True(counts.Min() / 12d > 2, "Every subject receives more than two complete frames per second.");
            Assert.InRange(counts.Max() - counts.Min(), 0, 1);
            Assert.True(maximumGap[lane] < .5, $"Lane {lane} maximum gap {maximumGap[lane]:F3}s reaches expiry.");
            _output.WriteLine($"subjects={players}, payload={capacity}, lane={lane}, minHz={counts.Min() / 12d:F3}, maxGap={maximumGap[lane]:F3}s");
        }
        _output.WriteLine($"peakRollingPackets={peakPackets}, peakRollingBytes={peakBytes}");
    }

    [Fact]
    public void ProjectionKeepsCompleteAuthorizedComponentsAndCannotMutateTheOwnerFrame()
    {
        var original = new PoseFrame("anomaly.classd", 1, new[] {
            new PoseBone(0, PoseComponents.Position, 1, 2, 3, 0, 0, 0, 1, .4f, true),
            new PoseBone(5, PoseComponents.Rotation, 0, 0, 0, .1f, .2f, .3f, .9f, .7f),
            new PoseBone(50, PoseComponents.Position | PoseComponents.Rotation, 1, 2, 3, 0, 0, 0, 1) });
        var projected = PoseProjectionPolicy.Project(original, new HashSet<ushort> { 0, 5 });
        Assert.Equal(3, original.Bones.Count);
        Assert.Equal(original.Bones.Take(2), projected.Bones);
        Assert.Same(original, PoseProjectionPolicy.Project(original, null));
    }

    [Fact]
    public void SampleLanesCannotStealReservationsAndRejectInvalidTime()
    {
        var budget = new PoseTrafficBudget();
        Assert.True(budget.TryChargeSamples(1, 8, PoseTrafficBudget.CosmeticBytesPerSecond / 20));
        Assert.False(budget.TryChargeSamples(1, 1, 1000));
        Assert.True(budget.TryChargeSamples(1, 8, PoseTrafficBudget.CombatBytesPerSecond / 20, combat: true));
        Assert.False(budget.TryChargeSamples(double.NaN, 1, 1, combat: true));
        Assert.False(budget.TryChargeSamples(.9, 1, 1, combat: true));
        Assert.True(budget.TryCharge(1, PoseTrafficBudget.ReservedControlPackets, PoseTrafficBudget.ReservedControlBytes));
    }

    private static PoseFrame Frame(int count) => new PoseFrame("anomaly.classd", 1,
        Enumerable.Range(0, count).Select(i => new PoseBone((ushort)i,
            PoseComponents.Position | PoseComponents.Rotation, 1, 2, 3, 0, 0, 0, 1)));
}
