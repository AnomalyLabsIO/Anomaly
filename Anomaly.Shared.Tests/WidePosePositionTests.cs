using System;
using System.IO;
using Anomaly.Shared.Capabilities;
using Anomaly.Shared.Poses;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class WidePosePositionTests
{
    [Theory]
    [InlineData(32.767f, false)]
    [InlineData(-32.767f, false)]
    [InlineData(32.768f, true)]
    [InlineData(38.566914f, true)]
    [InlineData(327.67f, true)]
    [InlineData(-327.67f, true)]
    public void WidePositionsRetainModelUnitsWithoutGrowingTheBoneEncoding(float value, bool wide)
    {
        var input = new PoseBone(0, PoseComponents.Position | PoseComponents.Rotation, .12345f, value, -value, 0, 0, 0, 1, .7f, true);
        using (var bytes = new MemoryStream())
        {
            PoseWireCodec.WriteBones(new BinaryWriter(bytes), new[] { input });
            Assert.Equal(2 + PoseWireCodec.GetBoneSize(input), bytes.Length);
            Assert.Equal(wide, (bytes.ToArray()[3] & 8) != 0);
            bytes.Position = 0;
            PoseBone decoded = Assert.Single(PoseWireCodec.ReadBones(new BinaryReader(bytes)));
            float tolerance = wide ? .00501f : .000501f;
            Assert.InRange(Math.Abs(decoded.X - input.X), 0, tolerance);
            Assert.InRange(Math.Abs(decoded.Y - input.Y), 0, tolerance);
            Assert.InRange(Math.Abs(decoded.Z - input.Z), 0, tolerance);
            Assert.Equal(input.Components, decoded.Components);
            Assert.True(decoded.IncludeLocalPlayer);
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(16)]
    public void WideFlagWithoutPositionAndUnknownFlagsAreRejected(byte flags)
    {
        byte[] bytes = { 1, 0, 0, flags, 255 };
        Assert.Throws<InvalidDataException>(() => PoseWireCodec.ReadBones(new BinaryReader(new MemoryStream(bytes))));
    }

    [Fact]
    public void CapabilityRequirementRejectsOldDecodersButUpdatedClientsStillSatisfyOldServers()
    {
        foreach (string id in new[] { "anomaly:player-pose-renderer", "anomaly:player-combat-pose" })
        {
            var old = new CapabilityDescriptor(id, 1, 0);
            var updated = new CapabilityDescriptor(id, 1, PoseWireCodec.CapabilityMinor);
            Assert.False(old.Satisfies(updated));
            Assert.True(updated.Satisfies(old));
        }
    }

    [Fact]
    public void RigidStockAnchorsAreAuthoredAndInsideTheNewTransmissionBound()
    {
        Assert.True(PoseRigidAnchorCatalog.TryGetHips("anomaly.scp049", out var doctor));
        Assert.InRange(doctor.Y, 38.56f, 38.57f);
        Assert.True(doctor.Y < PoseBone.MaximumPosition);
        Assert.True(PoseRigidAnchorCatalog.TryGetHips("anomaly.scp096", out var shy));
        Assert.InRange(shy.Y, 1.20f, 1.21f);
        Assert.False(PoseRigidAnchorCatalog.TryGetHips("anomaly.classd", out _));
    }
}
