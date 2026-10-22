using Anomaly.Shared.Poses;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class PlayerPoseRigCatalogTests
{
    [Fact]
    public void SupportedRoleFamiliesHaveBoundedUniqueStableBoneContracts()
    {
        sbyte[] roles = { 1, 6, 14, 15, 4, 11, 12, 13, 8, 18, 19, 20, 5, 10, 3, 23, 9, 16 };
        Assert.Equal(11, PoseRigCatalog.All.Count);
        foreach (sbyte role in roles)
        {
            Assert.True(PoseRigCatalog.TryGetForRole(role, out var rig));
            Assert.True(PoseRigCatalog.TryGet(rig.Id, rig.Version, out var same));
            Assert.Same(rig, same);
            Assert.Equal(rig.Bones.Count, rig.Bones.Select(b => b.Id).Distinct().Count());
            Assert.Equal(rig.Bones.Count, rig.Bones.Select(b => b.SemanticName).Distinct().Count());
            Assert.All(rig.Bones, bone => Assert.InRange(bone.Id, (ushort)0, (ushort)(PoseFrame.MaxBones - 1)));
            Assert.True(rig.TryGetBone(0, out var pelvis));
            Assert.Equal("Hips", pelvis.SemanticName);
            Assert.True(rig.TryGetBone(5, out var head));
            Assert.Equal("Head", head.SemanticName);
            Assert.True(rig.TryGetBone(9, out var left));
            Assert.Equal("LeftHand", left.SemanticName);
            Assert.True(rig.TryGetBone(13, out var right));
            Assert.Equal("RightHand", right.SemanticName);
        }
    }

    [Fact]
    public void MissingVersionsAndNonBodyRolesFailClosed()
    {
        foreach (sbyte role in new sbyte[] { -1, 0, 2, 7, 17, 21, 22, 24, 25, 26, 27, 28, 29 })
            Assert.False(PoseRigCatalog.TryGetForRole(role, out _));
        foreach (var rig in PoseRigCatalog.All)
        {
            Assert.False(PoseRigCatalog.TryGet(rig.Id, 0, out _));
            Assert.False(PoseRigCatalog.TryGet(rig.Id, 2, out _));
            Assert.False(rig.TryGetBone(127, out _));
        }
        Assert.False(PoseRigCatalog.TryGet("unregistered.model", 1, out _));
    }

    [Fact]
    public void CatalogCollectionsCannotBeChangedByConsumers()
    {
        Assert.Throws<NotSupportedException>(() => ((IList<PoseRigDescriptor>)PoseRigCatalog.All).Clear());
        foreach (var rig in PoseRigCatalog.All)
            Assert.Throws<NotSupportedException>(() => ((IList<PoseBoneDescriptor>)rig.Bones).Clear());
    }

    [Fact]
    public void NonHumanoidRigHelpersRetainTheirAuthoredChain()
    {
        Assert.True(PoseRigCatalog.TryGetForRole(16, out var dog));
        Assert.True(dog.TryGetBone(64, out var wrist));
        Assert.EndsWith("DEF-LowerArm.L/DEF-Wrist.L", wrist.Path);
        Assert.True(dog.TryGetBone(9, out var hand));
        Assert.Equal(wrist.Path + "/DEF-Hand.L", hand.Path);
        Assert.True(PoseRigCatalog.TryGetForRole(9, out var shy));
        Assert.True(shy.TryGetBone(64, out var lowerNeck));
        Assert.True(shy.TryGetBone(4, out var upperNeck));
        Assert.Equal(lowerNeck.Path + "/Neck02", upperNeck.Path);
    }
}
