using Anomaly.Client.Api.Internal.Poses;
using Anomaly.Shared.Poses;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class CombatPoseScopeTests
{
    [Fact]
    public void PartialAcquisitionFailureRollsBackEveryMutationBeforeSynchronizing()
    {
        int first = 1, second = 2, syncs = 0;
        Assert.Throws<InvalidOperationException>(() => PoseMutationScope.Begin(scope =>
        {
            int oldFirst = first;
            scope.AddRestore(() => first = oldFirst);
            first = 10;
            int oldSecond = second;
            scope.AddRestore(() => second = oldSecond);
            second = 20;
            throw new InvalidOperationException("native acquisition failed");
        }, () => { Assert.Equal(1, first); Assert.Equal(2, second); syncs++; }));
        Assert.Equal(1, syncs);
    }

    [Fact]
    public void NestedScopesAndFinalizerDisposalRestoreTheirOwnBaselineExactlyOnce()
    {
        int bone = 1, syncs = 0;
        var outer = PoseMutationScope.Begin(scope => { int old = bone; scope.AddRestore(() => bone = old); bone = 2; }, () => syncs++);
        var inner = PoseMutationScope.Begin(scope => { int old = bone; scope.AddRestore(() => bone = old); bone = 3; }, () => syncs++);
        Assert.Equal(3, bone);
        inner.Dispose(); inner.Dispose();
        Assert.Equal(2, bone);
        outer.Dispose(); outer.Dispose();
        Assert.Equal(1, bone);
        Assert.Equal(4, syncs);
    }

    [Fact]
    public void OneBrokenRestoreCannotPreventOtherBodiesOrPhysicsSynchronization()
    {
        int bone = 1, syncs = 0;
        var scope = PoseMutationScope.Begin(journal =>
        {
            journal.AddRestore(() => bone = 1);
            journal.AddRestore(() => throw new InvalidOperationException("destroyed object"));
            bone = 2;
        }, () => syncs++);
        Assert.Throws<AggregateException>(scope.Dispose);
        Assert.Equal(1, bone);
        Assert.Equal(2, syncs);
        scope.Dispose();
        Assert.Equal(2, syncs);
    }

    [Fact]
    public void CombatAndCosmeticReceiversNeverShareAuthorizationAndCombatDoesNotFadeStaleBodies()
    {
        var cosmetic = new PoseReceiver();
        var combat = new PoseReceiver();
        cosmetic.Reset(1); combat.Reset(1);
        var binding = new PoseBinding { NetId = 1, ModelGeneration = 1, BindingRevision = 1,
            RoleId = 1, RigId = "anomaly.classd", SchemaVersion = 1 };
        Assert.True(cosmetic.Bind(binding)); Assert.True(combat.Bind(binding));
        var activate = new PoseControl { Operation = PoseControlOperation.Activate, Binding = binding,
            OverrideGeneration = 1, Sequence = 1, ServerTime = 10,
            Frame = new PoseFrame(binding.RigId, 1, new[] { new PoseBone(5, PoseComponents.Rotation, 0, 0, 0, 0, 0, 0, 1) }) };
        Assert.True(cosmetic.Apply(activate, 10));
        Assert.False(combat.TryGetForCombat(1, 10, out _, out _));
        Assert.True(combat.Apply(activate, 10));
        Assert.True(combat.TryGetForCombat(1, 10.49, out _, out _));
        Assert.False(combat.TryGetForCombat(1, 10.51, out _, out _));
        Assert.True(cosmetic.TryGet(1, 10.51, out _, out _));
        Assert.True(combat.Apply(new PoseControl { Operation = PoseControlOperation.Clear, Binding = binding, OverrideGeneration = 1 }, 10.52));
        Assert.False(combat.Apply(activate, 10.53));
        Assert.True(cosmetic.TryGet(1, 10.53, out _, out _));
    }
}
