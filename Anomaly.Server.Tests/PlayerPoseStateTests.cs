using System;
using System.Linq;
using System.Threading;
using Anomaly.Server.Api.Features;
using Anomaly.Server.Poses;
using Anomaly.Shared.Poses;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class PlayerPoseStateTests
{
    private static PoseFrame Frame(float x, params ushort[] ids) => new PoseFrame("anomaly.classd", 1,
        ids.Select(id => new PoseBone(id, PoseComponents.Position | PoseComponents.Rotation, x, 0, 0, 0, 0, 0, 1)));

    [Fact]
    public void PriorityIsPerBoneAndTiesFavorEarlierHandle()
    {
        var state = new PlayerPoseState();
        Assert.True(state.TryCreate(1, 2, 3, Frame(1, 0, 1), new PlayerPoseOptions(), 0, out ulong first));
        Assert.True(state.TryCreate(2, 2, 3, Frame(2, 1, 2), new PlayerPoseOptions { Priority = 2, Weight = .5f }, 0, out ulong second));
        Assert.True(state.TryCreate(3, 2, 3, Frame(3, 2), new PlayerPoseOptions { Priority = 2 }, 0, out _));
        var frame = state.Compose(2, 0).Frame;
        Assert.Equal(new[] { 1f, 2f, 2f }, frame.Bones.Select(b => b.X));
        Assert.Equal(.5f, frame.Bones[1].Weight);
        state.Remove(second, 2);
        frame = state.Compose(2, 0).Frame;
        Assert.Equal(new[] { 1f, 1f, 3f }, frame.Bones.Select(b => b.X));
        state.Remove(first, 99);
        Assert.True(state.TryGet(first, 1, out _));
    }

    [Fact]
    public void CompleteReplacementReleasesMissingBonesAndIncrementsControlRevision()
    {
        var state = new PlayerPoseState();
        state.TryCreate(1, 2, 3, Frame(1, 0, 1), new PlayerPoseOptions(), 0, out ulong id);
        ulong originalRevision = state.Compose(2, 0).Revision;
        Assert.True(state.TryGet(id, 1, out var entry));
        state.Update(entry, Frame(2, 0, 1), .1);
        Assert.Equal(originalRevision, state.Compose(2, .1).Revision);
        state.Update(entry, Frame(3, 0), .2);
        Assert.True(state.Compose(2, .2).Revision > originalRevision);
        Assert.Single(state.Compose(2, .2).Frame.Bones);
    }

    [Fact]
    public void ExpirationInvalidatesOnlyInactiveHandleAndRevealsRemainingOwner()
    {
        var state = new PlayerPoseState();
        state.TryCreate(1, 2, 3, Frame(1, 0), new PlayerPoseOptions { InactivityTimeout = Timeout.InfiniteTimeSpan }, 0, out ulong persistent);
        state.TryCreate(2, 2, 3, Frame(2, 0), new PlayerPoseOptions { Priority = 1 }, 0, out ulong tracking);
        Assert.Equal(2f, state.Compose(2, 0).Frame.Bones[0].X);
        state.Expire(.5);
        Assert.False(state.TryGet(tracking, 2, out _));
        Assert.True(state.TryGet(persistent, 1, out _));
        Assert.Equal(1f, state.Compose(2, .5).Frame.Bones[0].X);
    }

    [Fact]
    public void ClearModelReplacementAndRoundResetNeverReuseHandleIdentities()
    {
        var state = new PlayerPoseState();
        state.TryCreate(1, 2, 3, Frame(1, 0), new PlayerPoseOptions(), 0, out ulong a);
        state.TryGet(a, 1, out var entry);
        state.Clear(entry);
        Assert.False(state.TryGet(a, 1, out _));
        Assert.Null(state.Compose(2, 0).Frame);
        state.TryCreate(1, 2, 3, Frame(1, 0), new PlayerPoseOptions(), 0, out ulong b);
        state.RemovePlayer(2);
        Assert.False(state.TryGet(b, 1, out _));
        state.Reset();
        state.TryCreate(1, 2, 4, Frame(1, 0), new PlayerPoseOptions(), 0, out ulong c);
        Assert.True(c > b && b > a);
    }

    [Fact]
    public void OptionsAreCopiedAndPersistentFramesRefreshHealthyStream()
    {
        var state = new PlayerPoseState();
        var options = new PlayerPoseOptions { InactivityTimeout = Timeout.InfiniteTimeSpan, IncludeLocalPlayer = true };
        state.TryCreate(1, 2, 3, Frame(1, 0), options, 0, out _);
        options.Weight = 0;
        var first = state.Compose(2, 0);
        ulong sequence = first.Sequence, revision = first.Revision;
        Assert.Equal(1f, first.Frame.Bones[0].Weight);
        Assert.True(first.Frame.Bones[0].IncludeLocalPlayer);
        Assert.True(state.Compose(2, .21).Sequence > sequence);
        Assert.Equal(revision, state.Compose(2, .21).Revision);
        state.ResetInterpolation(2);
        Assert.True(state.Compose(2, .22).Revision > revision);
    }

    [Fact]
    public void ZeroWeightDoesNotBlockLowerPriorityOwnerAndOwnershipIsBounded()
    {
        var state = new PlayerPoseState();
        state.TryCreate(1, 2, 3, Frame(1, 0), new PlayerPoseOptions(), 0, out _);
        state.TryCreate(2, 2, 3, Frame(2, 0), new PlayerPoseOptions { Priority = 10, Weight = 0 }, 0, out _);
        Assert.Equal(1f, state.Compose(2, 0).Frame.Bones[0].X);
        for (int i = 2; i < PlayerPoseState.MaximumOverridesPerPlayer; i++)
            Assert.True(state.TryCreate(i + 1, 2, 3, Frame(1, 0), new PlayerPoseOptions(), 0, out _));
        Assert.False(state.TryCreate(100, 2, 3, Frame(1, 0), new PlayerPoseOptions(), 0, out _));
        state.RemoveOwner(1);
        Assert.True(state.TryCreate(100, 2, 3, Frame(1, 0), new PlayerPoseOptions(), 0, out _));
    }

    [Fact]
    public void RecipientBudgetUsesRollingWindowAndSharedByteAndPacketCeilings()
    {
        var budget = new PoseTrafficBudget();
        Assert.True(budget.TryCharge(0, 100, 10000));
        Assert.True(budget.TryCharge(.1, PoseTrafficBudget.MaximumPacketsPerSecond - 100, 10000));
        Assert.False(budget.TryCharge(.99, 1, 1));
        Assert.True(budget.TryCharge(1, 100, 10000));
        Assert.False(budget.TryCharge(1, 1, 1));
        var bytes = new PoseTrafficBudget();
        Assert.True(bytes.TryCharge(0, 1, PoseTrafficBudget.MaximumBytesPerSecond));
        Assert.False(bytes.TryCharge(.99, 1, 1));
        Assert.True(bytes.TryCharge(1, 1, 1));
    }

    [Fact]
    public void AnimatedWeightUsesSamplesWithoutReliableControlChurn()
    {
        var state = new PlayerPoseState();
        state.TryCreate(1, 2, 3, Frame(1, 0), new PlayerPoseOptions(), 0, out ulong id);
        ulong revision = state.Compose(2, 0).Revision;
        state.TryGet(id, 1, out var entry);
        state.Update(entry, new PoseFrame("anomaly.classd", 1, new[] {
            new PoseBone(0, PoseComponents.Position | PoseComponents.Rotation, 1, 0, 0, 0, 0, 0, 1, .4f) }), .1);
        Assert.Equal(revision, state.Compose(2, .1).Revision);
        Assert.Equal(.4f, state.Compose(2, .1).Frame.Bones[0].Weight);
    }

    [Fact]
    public void NativeLifetimeObservationIsIdempotentButPooledReuseAndNetIdReuseAdvance()
    {
        var epochs = new PoseModelEpochs();
        object hub = new object();
        ulong first = epochs.Observe(1, hub, 10);
        Assert.Equal(first, epochs.Observe(1, hub, 10)); // send inside native Init, then its postfix
        ulong pooled = epochs.Observe(1, hub, 11);
        Assert.True(pooled > first);
        ulong reusedId = epochs.Observe(1, new object(), 11);
        Assert.True(reusedId > pooled);
        epochs.Clear();
        Assert.True(epochs.Observe(1, hub, 10) > reusedId);
    }

    [Fact]
    public void FullPoseChunksStayWithinRecipientPacketAndReconstructEveryBone()
    {
        var binding = new PoseBinding { NetId = 1, ModelGeneration = 2, BindingRevision = 3,
            RoleId = 1, RigId = "anomaly.classd", SchemaVersion = 1 };
        var frame = Frame(1, Enumerable.Range(0, PoseFrame.MaxBones).Select(i => (ushort)i).ToArray());
        var chunks = PoseSamplePacketizer.Chunk(binding, 4, 5, 1, frame, 1000);
        Assert.True(chunks.Count > 1);
        Assert.Equal(frame.Bones.Select(b => b.BoneId), chunks.SelectMany(c => c.Bones).Select(b => b.BoneId));
        Assert.All(chunks, c => Assert.Equal(chunks.Count, c.ChunkCount));
        var packets = PoseSamplePacketizer.Pack(1, chunks, 1000);
        Assert.All(packets, packet => Assert.True(PoseWireCodec.MessageHeaderBytes + packet.Chunks.Sum(PoseWireCodec.GetSampleChunkSize) <= 1000));
        Assert.Equal(chunks.Count, packets.Sum(packet => packet.Chunks.Count));
    }

    [Fact]
    public void SmallSamplesBatchAcrossPlayersAndWholeSampleReservationPreventsFirstChunkStarvation()
    {
        var a = new PoseBinding { NetId = 1, ModelGeneration = 2, BindingRevision = 3,
            RoleId = 1, RigId = "anomaly.classd", SchemaVersion = 1 };
        var b = a.Clone(); b.NetId = 2;
        var first = PoseSamplePacketizer.Chunk(a, 1, 1, 0, Frame(1, 0), 1000);
        var second = PoseSamplePacketizer.Chunk(b, 1, 1, 0, Frame(1, 0), 1000);
        Assert.Single(PoseSamplePacketizer.Pack(1, first.Concat(second), 1000));
        var large = PoseSamplePacketizer.Chunk(a, 1, 2, 0,
            Frame(1, Enumerable.Range(0, PoseFrame.MaxBones).Select(i => (ushort)i).ToArray()), 1000);
        int count = PoseSamplePacketizer.Pack(1, large, 1000).Count;
        var budget = new PoseTrafficBudget();
        Assert.True(budget.TryCharge(0, PoseTrafficBudget.MaximumPacketsPerSecond - 1, 12700));
        Assert.False(budget.TryCharge(.5, count, count * 1128));
        Assert.True(budget.TryCharge(1, count, count * 1128));
    }

    [Fact]
    public void SamplesCannotConsumeReservedControlTraffic()
    {
        var budget = new PoseTrafficBudget();
        Assert.True(budget.TryChargeSamples(0, PoseTrafficBudget.MaximumPacketsPerSecond - PoseTrafficBudget.ReservedControlPackets,
            1));
        Assert.False(budget.TryChargeSamples(.1, 1, 1));
        Assert.True(budget.TryCharge(.1, PoseTrafficBudget.ReservedControlPackets, PoseTrafficBudget.ReservedControlBytes));
        Assert.False(budget.TryCharge(.1, 1, 1));
        Assert.True(budget.TryChargeSamples(1.1, 1, 1));
    }

    [Fact]
    public void FailedClearRetainsTombstoneThenSuccessfulClearPrunesAndRoundResetsAllHistory()
    {
        var ledger = new PoseTargetLedger();
        var target = ledger.GetOrAdd(1, out _);
        target.Active = true;
        ledger.Retire(1);
        ledger.Prune(1); // reliable clear could not yet be queued
        Assert.True(ledger.Targets.ContainsKey(1));
        target.Active = false; // reliable clear is now queued
        ledger.Prune(1);
        Assert.Empty(ledger.Targets);
        ledger.GetOrAdd(2, out _).Active = true;
        ledger.Reset();
        Assert.Empty(ledger.Targets);
    }

    [Fact]
    public void SustainedFailedClearsRemainBoundedAndRequireResetBarrierOnOverflow()
    {
        var ledger = new PoseTargetLedger();
        for (uint i = 1; i <= PoseTargetLedger.MaximumTargets; i++)
        {
            ledger.GetOrAdd(i, out bool reset).Active = true;
            Assert.False(reset);
            ledger.Retire(i);
        }
        ledger.GetOrAdd(1000, out bool resetRequired);
        Assert.True(resetRequired);
        Assert.Single(ledger.Targets);
    }
}
