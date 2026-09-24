using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class MessageContractValidationTests
{
    [Fact]
    public void Protocol_v6_registers_the_required_facility_snapshot_on_both_peers()
    {
        Assert.Equal((byte)6, AnomalyProtocol.CurrentVersion);
        AnomalyMessageRegistry.RegisterDefaults();
        uint hash = AnomalyMessageRegistry.GetHash("anomaly:facility:snapshot");
        Assert.True(AnomalyMessageRegistry.TryCreate(hash, out var message));
        Assert.IsType<FacilitySnapshotMessage>(message);
        Assert.True(AnomalyMessageRegistry.TryGetSendContract(
            message, AnomalyMessageDirection.ServerToClient, out uint sendHash, out var channel, out _));
        Assert.Equal(hash, sendHash);
        Assert.Equal(MessageChannel.ReliableOrdered, channel);
        Assert.False(AnomalyMessageRegistry.TryGetSendContract(
            message, AnomalyMessageDirection.ClientToServer, out _, out _, out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(int.MaxValue)]
    public void Undefined_channel_registration_leaves_no_partial_contract(int channel)
    {
        string name = NewName();
        Assert.Throws<InvalidOperationException>(() => AnomalyMessageRegistry.Register(
            name, () => new MutableMessage(name) { TransportChannel = (MessageChannel)channel }));
        Assert.Throws<KeyNotFoundException>(() => AnomalyMessageRegistry.GetHash(name));
        Assert.False(AnomalyMessageRegistry.TryGetChannel(AnomalyMessageRegistry.ComputeHash(name), out _));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("channel")]
    [InlineData("direction")]
    [InlineData("phase")]
    [InlineData("bound")]
    public void Send_and_factory_reject_a_changed_registered_contract(string change)
    {
        var message = new MutableMessage(NewName());
        AnomalyMessageRegistry.Register(message.MessageName, () => message);
        uint hash = AnomalyMessageRegistry.GetHash(message.MessageName);
        Assert.True(AnomalyMessageRegistry.TryGetSendContract(
            message, AnomalyMessageDirection.ClientToServer, out _, out _, out _));

        switch (change)
        {
            case "name": message.MessageName = NewName(); break;
            case "channel": message.TransportChannel = MessageChannel.Unreliable; break;
            case "direction": message.Direction = AnomalyMessageDirection.ServerToClient; break;
            case "phase": message.AuthPhase = (AnomalyMessageAuthPhase)0; break;
            case "bound": message.MaxPayloadBytes++; break;
        }

        Assert.False(AnomalyMessageRegistry.TryGetSendContract(
            message, AnomalyMessageDirection.ClientToServer, out _, out _, out _));
        Assert.False(AnomalyMessageRegistry.TryCreate(hash, out IAnomalyMessage created));
        Assert.Null(created);
    }

    [Fact]
    public void Registered_name_cannot_be_borrowed_by_another_runtime_type_or_direction()
    {
        string name = NewName();
        AnomalyMessageRegistry.Register(name, () => new MutableMessage(name));
        Assert.False(AnomalyMessageRegistry.TryGetSendContract(
            new AlternateMessage(name), AnomalyMessageDirection.ClientToServer, out _, out _, out _));
        Assert.False(AnomalyMessageRegistry.TryGetSendContract(
            new MutableMessage(name), AnomalyMessageDirection.ServerToClient, out _, out _, out _));
        Assert.False(AnomalyMessageRegistry.TryGetSendContract(
            null, AnomalyMessageDirection.ClientToServer, out _, out _, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Factory_failure_does_not_escape_the_receive_boundary(bool throws)
    {
        string name = NewName();
        bool registered = false;
        AnomalyMessageRegistry.Register(name, () => !registered ? new MutableMessage(name) :
            throws ? throw new InvalidOperationException("broken factory") : null);
        registered = true;
        Assert.False(AnomalyMessageRegistry.TryCreate(AnomalyMessageRegistry.GetHash(name), out var message));
        Assert.Null(message);
    }

    private static string NewName() => "tests:contract:" + Guid.NewGuid().ToString("N");

    private class MutableMessage : IAnomalyMessage
    {
        public MutableMessage(string name) => MessageName = name;
        public string MessageName { get; set; }
        public MessageChannel TransportChannel { get; set; } = MessageChannel.ReliableOrdered;
        public AnomalyMessageDirection Direction { get; set; } = AnomalyMessageDirection.ClientToServer;
        public AnomalyMessageAuthPhase AuthPhase { get; set; } = AnomalyMessageAuthPhase.AuthenticatedSession;
        public int MaxPayloadBytes { get; set; } = 4;
        public void Serialize(BinaryWriter writer) => writer.Write(42);
        public void Deserialize(BinaryReader reader) => reader.ReadInt32();
    }

    private sealed class AlternateMessage : MutableMessage
    {
        public AlternateMessage(string name) : base(name) { }
    }
}
