using Anomaly.Client.Api.Networking;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Poses;
using Xunit;

namespace Anomaly.Client.Api.Tests;

[Collection("Registration lifecycle")]
public sealed class PoseTransportAdmissionTests : IDisposable
{
    public PoseTransportAdmissionTests()
    {
        AnomalyMessageRegistry.RegisterDefaults();
        AnomalyReplayTable.Reset();
        AnomalyMessaging.ConfigureTransportSession(new byte[32], new byte[32], new byte[16], new byte[32]);
    }

    [Fact]
    public void Stale_and_duplicate_authenticated_samples_drop_without_poisoning_the_session()
    {
        int received = 0;
        void Observe(IAnomalyMessage _) => received++;
        AnomalyMessaging.MessageReceived += Observe;
        try
        {
            Receive(Sample(), 1000);
            for (uint i = 1; i <= 40; i++) Receive(Sample(), i);
            for (int i = 0; i < 40; i++) Receive(Sample(), 1000);
            Assert.Equal(1, received);
            Assert.True(SessionActive());
            Assert.Equal(1, AnomalyReplayTable.InboundRateLimiter.RegisterProtocolViolation().DroppedInWindow);
        }
        finally { AnomalyMessaging.MessageReceived -= Observe; }
    }

    [Fact]
    public void Normal_and_pose_bursts_keep_separate_allowances_with_bounded_raw_capacity()
    {
        string name = "tests:pose-load:" + Guid.NewGuid().ToString("N");
        AnomalyMessageRegistry.Register(name, () => new Notice(name));
        int normal = 0, controls = 0, samples = 0;
        void Observe(IAnomalyMessage message)
        {
            if (message is Notice) normal++;
            else if (message is PlayerPoseControlMessage or PlayerCombatPoseControlMessage) controls++;
            else if (message is PlayerPoseSampleMessage or PlayerCombatPoseSampleMessage) samples++;
        }
        AnomalyMessaging.MessageReceived += Observe;
        try
        {
            // The production raw gate sees opaque ciphertext before the dispatch method tested here.
            // Both bounded decoded bursts must fit that combined pre-decrypt allowance.
            for (uint i = 1; i <= 400; i++) ReceiveWithRawAdmission(new Notice(name), i);
            for (uint i = 1; i <= 200; i++)
            {
                ReceiveWithRawAdmission(Control(), 400 + i * 2 - 1);
                var combatControl = new PlayerCombatPoseControlMessage { RoundEpoch = 1 };
                combatControl.Controls.Add(new PoseControl { Operation = PoseControlOperation.ResetAll });
                ReceiveWithRawAdmission(combatControl, 400 + i * 2);
                ReceiveWithRawAdmission(Sample(), i * 2 - 1);
                ReceiveWithRawAdmission(new PlayerCombatPoseSampleMessage { RoundEpoch = 1 }, i * 2);
            }
            Assert.Equal(400, normal);
            Assert.Equal(400, controls);
            Assert.Equal(400, samples);
            Assert.True(SessionActive());
            Assert.Equal(1, AnomalyReplayTable.InboundRateLimiter.RegisterProtocolViolation().DroppedInWindow);
            Assert.Equal(1, AnomalyReplayTable.RawInboundRateLimiter.RegisterProtocolViolation().DroppedInWindow);
        }
        finally { AnomalyMessaging.MessageReceived -= Observe; }
    }

    [Fact]
    public void Optional_decoded_overload_drops_without_consuming_normal_traffic_capacity()
    {
        string name = "tests:pose-after-overload:" + Guid.NewGuid().ToString("N");
        AnomalyMessageRegistry.Register(name, () => new Notice(name));
        int normal = 0, poses = 0;
        void Observe(IAnomalyMessage message) { if (message is Notice) normal++; else poses++; }
        AnomalyMessaging.MessageReceived += Observe;
        try
        {
            for (uint i = 1; i <= 4096; i++) Receive(Sample(), i);
            Receive(new Notice(name), 1);
            Assert.InRange(poses, 1, 4095);
            Assert.Equal(1, normal);
            Assert.True(SessionActive());
            Assert.Equal(1, AnomalyReplayTable.InboundRateLimiter.RegisterProtocolViolation().DroppedInWindow);
        }
        finally { AnomalyMessaging.MessageReceived -= Observe; }
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(uint.MaxValue)]
    public void Invalid_sample_counters_still_revoke_the_session(uint counter)
    {
        for (int i = 0; i < 32; i++) Receive(Sample(), counter);
        Assert.False(SessionActive());
    }

    [Theory]
    [InlineData("unauthenticated")]
    [InlineData("malformed")]
    [InlineData("oversized")]
    [InlineData("unknown")]
    public void Optional_allowance_does_not_exempt_invalid_messages(string failure)
    {
        for (uint i = 1; i <= 32; i++)
        {
            byte[] bytes = Encode(Sample(), i);
            if (failure == "malformed") Array.Clear(bytes, 8, sizeof(uint)); // Invalid round epoch.
            else if (failure == "oversized") Array.Resize(ref bytes, 8 + Sample().MaxPayloadBytes + 1);
            else if (failure == "unknown") BitConverter.GetBytes(AnomalyMessageRegistry.ComputeHash("tests:unregistered-pose" )).CopyTo(bytes, 4);
            AnomalyMessaging.OnReceived(bytes, encryptedAndAuthenticated: failure != "unauthenticated", localTransport: false);
        }
        Assert.False(SessionActive());
    }

    [Fact]
    public void Reliable_control_replays_keep_the_existing_strict_guard()
    {
        Receive(Control(), 1000);
        for (int i = 0; i < 32; i++) Receive(Control(), 1000);
        Assert.False(SessionActive());
    }

    private static void ReceiveWithRawAdmission(IAnomalyMessage message, uint counter)
    {
        byte[] bytes = Encode(message, counter);
        var raw = AnomalyReplayTable.RawInboundRateLimiter.TryAccept(0xFFE00000u,
            bytes.Length + 1 + AuthTransportCrypto.EnvelopeOverhead);
        Assert.True(raw.Allowed);
        AnomalyMessaging.OnReceived(bytes, encryptedAndAuthenticated: true, localTransport: false);
    }

    private static void Receive(IAnomalyMessage message, uint counter) =>
        AnomalyMessaging.OnReceived(Encode(message, counter), encryptedAndAuthenticated: true, localTransport: false);
    private static byte[] Encode(IAnomalyMessage message, uint counter)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(counter); writer.Write(AnomalyMessageRegistry.GetHash(message.MessageName)); message.Serialize(writer);
        return stream.ToArray();
    }
    private static PlayerPoseSampleMessage Sample() => new() { RoundEpoch = 1 };
    private static PlayerPoseControlMessage Control()
    {
        var message = new PlayerPoseControlMessage { RoundEpoch = 1 };
        message.Controls.Add(new PoseControl { Operation = PoseControlOperation.ResetAll });
        return message;
    }
    private static bool SessionActive() => AnomalyMessaging.TryEncryptClientEnvelope(Array.Empty<byte>(), out _);
    public void Dispose() { AnomalyMessaging.ClearTransportSession(); AnomalyReplayTable.Reset(); }

    private sealed class Notice : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        internal Notice(string name) => MessageName = name;
        public string MessageName { get; }
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public override int MaxPayloadBytes => 0;
        public void Serialize(BinaryWriter writer) { }
        public void Deserialize(BinaryReader reader) { }
    }
}
