using System.IO;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Poses;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class CombatPoseContractTests
{
    [Fact]
    public void CombatContractsAreSeparateServerOnlyHashesAndUseTheBoundedPoseCodec()
    {
        AnomalyMessageRegistry.RegisterDefaults();
        Assert.NotEqual(AnomalyMessageRegistry.GetHash("anomaly:pose:control"), AnomalyMessageRegistry.GetHash("anomaly:combat-pose:control"));
        Assert.NotEqual(AnomalyMessageRegistry.GetHash("anomaly:pose:sample"), AnomalyMessageRegistry.GetHash("anomaly:combat-pose:sample"));
        var message = new PlayerCombatPoseControlMessage { RoundEpoch = 3 };
        message.Controls.Add(new PoseControl { Operation = PoseControlOperation.Activate,
            Binding = new PoseBinding { NetId = 4, ModelGeneration = 5, BindingRevision = 6, RoleId = 1, RigId = "anomaly.classd", SchemaVersion = 1 },
            OverrideGeneration = 7, Sequence = 8, ServerTime = 9,
            Frame = new PoseFrame("anomaly.classd", 1, new[] { new PoseBone(5, PoseComponents.Position | PoseComponents.Rotation, 0.1f, 1.5f, 0, 0, 0, 0, 1) }) });
        Assert.Equal(AnomalyMessageDirection.ServerToClient, message.Direction);
        Assert.Equal(MessageChannel.ReliableOrdered, message.TransportChannel);
        using (var bytes = new MemoryStream())
        {
            message.Serialize(new BinaryWriter(bytes)); bytes.Position = 0;
            var received = new PlayerCombatPoseControlMessage(); received.Deserialize(new BinaryReader(bytes));
            Assert.Equal(message.RoundEpoch, received.RoundEpoch);
            Assert.True(received.Controls[0].Binding.Matches(message.Controls[0].Binding));
            Assert.Equal(7UL, received.Controls[0].OverrideGeneration);
            Assert.Equal(8UL, received.Controls[0].Sequence);
            Assert.Single(received.Controls[0].Frame.Bones);
            Assert.Equal(bytes.Length, bytes.Position);
        }
        Assert.Equal(AnomalyMessageDirection.ServerToClient, new PlayerCombatPoseSampleMessage().Direction);
        Assert.Equal(MessageChannel.Unreliable, new PlayerCombatPoseSampleMessage().TransportChannel);
    }
}
