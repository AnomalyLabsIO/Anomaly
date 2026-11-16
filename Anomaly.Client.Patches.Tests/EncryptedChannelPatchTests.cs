using System.Reflection;
using System.Text;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Auth;
using Anomaly.Client.Patches.Auth;
using Anomaly.Client.Patches.Mirror;
using Anomaly.Shared.Auth;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMirror;
using Xunit;
using static Il2Cpp.EncryptedChannelManager;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Anomaly.Client.Patches.Tests;

public sealed class EncryptedChannelPatchTests : IDisposable
{
    private const ushort MessageId = 9934;
    private readonly ReferenceHub _hub = new();
    private readonly List<EncryptedMessage> _received = new();

    public EncryptedChannelPatchTests()
    {
        EncryptedChannelHandlerFix.ResetConnectionState();
        NetworkClient.handlers = new HandlerDictionary();
        NetworkServer.active = false;
        AuthHandler.UsingAnomalyAuth = true;
        LocalPlayer.Instance = new LocalPlayer { ReferenceHub = _hub };
        PlayerAuthenticationManagerPatch.ResetPendingClientMessages();
        AnomalyReplayTable.Reset();
        AnomalyMessaging.HasSession = true;
        AnomalyMessaging.BulkReceiveExpected = null;
        AnomalyMessaging.Failures.Clear();
        AnomalyMessaging.CustomMessages.Clear();
        AnomalyMessaging.DecryptAttempts = 0;
        Anomaly.Client.Content.ConnectionContentBootstrap.IsAdmitted = false;
        AnomalyMessaging.LastBeforeContentReady = false;
        EncryptedMessageTransport.Attempts.Clear();
        EncryptedMessageTransport.DeliveryResult = true;
        EncryptedMessageTransport.ThrowOnSend = false;
        ClientChannelHandlers.Clear();
        ClientChannelHandlers[EncryptedChannel.GameConsole] =
            new EncryptedMessageClientHandler((message, _) => _received.Add(message));
        ReceiveEntries = 0;
        EncryptedChannelFunctions.Reads = 0;
        Harmony.Installed.Clear();
    }

    public void Dispose()
    {
        EncryptedChannelHandlerFix.ResetConnectionState();
        PlayerAuthenticationManagerPatch.ResetPendingClientMessages();
        LocalPlayer.Instance = null;
        NetworkServer.active = false;
        Anomaly.Client.Content.ConnectionContentBootstrap.IsAdmitted = false;
        ClientChannelHandlers.Clear();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public void CustomMessageAdmissionUsesContentCommitRatherThanHubSpawn(bool hubExists, bool admitted, bool localHost)
    {
        LocalPlayer.Instance = hubExists ? new LocalPlayer { ReferenceHub = _hub } : null;
        Anomaly.Client.Content.ConnectionContentBootstrap.IsAdmitted = admitted;
        NetworkServer.active = localHost;
        var plaintext = new byte[] { 0xFE, 1, 0, 0, 0, 42, 0, 0, 0 };
        var envelope = AuthTransportCrypto.Encrypt(AnomalyMessaging.Key, AnomalyMessaging.SessionId,
            AnomalyMessaging.Transcript, AuthTransportDirection.ServerToClient, plaintext);
        Receive(envelope);
        Assert.Single(AnomalyMessaging.CustomMessages);
        Assert.Equal(!localHost && !admitted, AnomalyMessaging.LastBeforeContentReady);
        Assert.Empty(AnomalyMessaging.Failures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Identical_intentional_sends_each_get_a_counter_and_delivery(bool bytes)
    {
        Assert.True(Send("repeat λ", bytes));
        Assert.True(Send("repeat λ", bytes));

        Assert.Equal(2u, _hub.encryptedChannelManager._txCounter);
        Assert.Equal(2, EncryptedMessageTransport.Attempts.Count);
        AssertPacket(0, 1, "repeat λ");
        AssertPacket(1, 2, "repeat λ");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equal_length_and_head_tail_do_not_hide_different_payloads(bool bytes)
    {
        Assert.True(Send("HEADfirst-TAIL", bytes));
        Assert.True(Send("HEADother-TAIL", bytes));
        Assert.Equal(2, EncryptedMessageTransport.Attempts.Count);
        AssertPacket(0, 1, "HEADfirst-TAIL");
        AssertPacket(1, 2, "HEADother-TAIL");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Failed_send_never_turns_an_identical_retry_into_success(bool bytes, bool throws)
    {
        EncryptedMessageTransport.DeliveryResult = false;
        EncryptedMessageTransport.ThrowOnSend = throws;

        Assert.False(Send("same failed message", bytes));
        Assert.False(Send("same failed message", bytes));
        Assert.Single(EncryptedMessageTransport.Attempts);
        Assert.False(AnomalyMessaging.HasSession);
        Assert.NotEmpty(AnomalyMessaging.Failures);

        // A fresh authenticated session can actually try the same content again.
        AnomalyMessaging.HasSession = true;
        EncryptedMessageTransport.DeliveryResult = true;
        EncryptedMessageTransport.ThrowOnSend = false;
        Assert.True(Send("same failed message", bytes));
        Assert.Equal(2, EncryptedMessageTransport.Attempts.Count);
        AssertPacket(1, 3, "same failed message");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Exhausted_counter_rejects_every_attempt_without_delivery(bool bytes)
    {
        _hub.encryptedChannelManager._txCounter = uint.MaxValue - 1;
        Assert.False(Send("repeat", bytes));
        Assert.False(Send("repeat", bytes));
        Assert.Empty(EncryptedMessageTransport.Attempts);
        Assert.Equal(uint.MaxValue - 1, _hub.encryptedChannelManager._txCounter);
    }

    [Fact]
    public void Authenticated_replay_is_rejected_after_charging_each_delivery()
    {
        var envelope = Envelope(1, "first");
        Receive(envelope);
        Receive((byte[])envelope.Clone());

        Assert.Single(_received);
        Assert.Equal(2, AnomalyMessaging.DecryptAttempts);
        Assert.Equal(1u, _hub.encryptedChannelManager._rxCounter);
        // Exactly one replay violation has already been charged.
        Assert.Equal(2, AnomalyReplayTable.RawInboundRateLimiter.RegisterProtocolViolation().DroppedInWindow);
    }

    [Fact]
    public void Same_envelope_prefix_with_modified_tag_is_authenticated_and_rejected()
    {
        var envelope = Envelope(1, "first");
        Receive(envelope);
        var tampered = (byte[])envelope.Clone();
        tampered[^1] ^= 1;
        Receive(tampered);

        Assert.Single(_received);
        Assert.Equal(2, AnomalyMessaging.DecryptAttempts);
        Assert.Equal(2, AnomalyReplayTable.RawInboundRateLimiter.RegisterProtocolViolation().DroppedInWindow);
    }

    [Fact]
    public void Equal_payloads_with_distinct_authenticated_counters_both_dispatch()
    {
        Receive(Envelope(1, "repeat"));
        Receive(Envelope(2, "repeat"));
        Assert.Equal(new uint[] { 1, 2 }, _received.Select(message => message.Counter));
        Assert.All(_received, message => Assert.Equal("repeat", Encoding.UTF8.GetString(message.Content)));
    }

    [Fact]
    public void Every_repeated_null_delivery_counts_as_a_protocol_violation()
    {
        Receive(null);
        Receive(null);
        Receive(null);
        Assert.Equal(0, AnomalyMessaging.DecryptAttempts);
        Assert.Equal(4, AnomalyReplayTable.RawInboundRateLimiter.RegisterProtocolViolation().DroppedInWindow);
    }

    [Fact]
    public void Repeated_registration_dispatches_once_and_restores_the_previous_owner()
    {
        int previousCalls = 0;
        var previous = new NetworkMessageDelegate((_, _, _) => previousCalls++);
        NetworkClient.handlers[MessageId] = previous;

        Assert.True(EncryptedChannelHandlerFix.EnsureRegistered());
        var bridge = NetworkClient.handlers[MessageId];
        Assert.True(EncryptedChannelHandlerFix.EnsureRegistered());
        Assert.Same(bridge, NetworkClient.handlers[MessageId]);
        Assert.Equal(2, NetworkClient.handlers.Writes); // initial owner + one publication
        bridge.Invoke(new NetworkConnection(), new NetworkReader
        {
            Packed = new EncryptedMessageOutside(Envelope(1, "one delivery"), SecurityLevel.EncryptedAndAuthenticated)
        }, 0);

        Assert.Single(_received);
        Assert.Equal(1, EncryptedChannelFunctions.Reads);
        Assert.Equal(1, ReceiveEntries);
        Assert.Equal(0, previousCalls);
        EncryptedChannelHandlerFix.ResetConnectionState();
        Assert.Same(previous, NetworkClient.handlers[MessageId]);
    }

    [Fact]
    public void Registration_failure_rolls_back_partial_publication_and_can_retry()
    {
        var previous = new NetworkMessageDelegate((_, _, _) => { });
        NetworkClient.handlers[MessageId] = previous;
        NetworkClient.handlers.FailNextPublication = true;
        Assert.False(EncryptedChannelHandlerFix.EnsureRegistered());
        Assert.False(EncryptedChannelHandlerFix.IsReady);
        Assert.Same(previous, NetworkClient.handlers[MessageId]);
        Assert.True(EncryptedChannelHandlerFix.EnsureRegistered());
        Assert.NotSame(previous, NetworkClient.handlers[MessageId]);
    }

    [Fact]
    public void Disconnect_does_not_overwrite_a_subsequent_handler_owner()
    {
        Assert.True(EncryptedChannelHandlerFix.EnsureRegistered());
        var replacement = new NetworkMessageDelegate((_, _, _) => { });
        NetworkClient.handlers[MessageId] = replacement;
        EncryptedChannelHandlerFix.ResetConnectionState();
        Assert.Same(replacement, NetworkClient.handlers[MessageId]);
    }

    [Fact]
    public void Malformed_bridge_input_fails_closed_without_receive_dispatch()
    {
        Assert.True(EncryptedChannelHandlerFix.EnsureRegistered());
        NetworkClient.handlers[MessageId].Invoke(new NetworkConnection(), new NetworkReader { ThrowOnRead = true }, 0);
        Assert.Equal(0, ReceiveEntries);
        Assert.Single(AnomalyMessaging.Failures);
        Assert.False(AnomalyMessaging.HasSession);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Required_patch_verification_rejects_missing_or_duplicate_own_registration(int count)
    {
        var target = Method(nameof(Target));
        var patch = Method(nameof(ExpectedPatch));
        var info = new HarmonyLib.Patches();
        for (int i = 0; i < count; i++) info.Prefixes.Add(new Patch("anomaly", patch));
        Harmony.Installed[target] = info;
        var error = Assert.Throws<InvalidOperationException>(() =>
            Core.VerifyPatchForTests("anomaly", target, patch, HarmonyPatchType.Prefix));
        Assert.Contains($"found {count}", error.Message);
    }

    [Fact]
    public void Required_patch_verification_matches_owner_method_and_patch_kind()
    {
        var target = Method(nameof(Target));
        var patch = Method(nameof(ExpectedPatch));
        var info = new HarmonyLib.Patches();
        info.Prefixes.Add(new Patch("other", patch));
        info.Prefixes.Add(new Patch("anomaly", Method(nameof(OtherPatch))));
        info.Postfixes.Add(new Patch("anomaly", patch));
        Harmony.Installed[target] = info;
        Assert.Throws<InvalidOperationException>(() => Core.VerifyPatchForTests("anomaly", target, patch, HarmonyPatchType.Prefix));
        Core.VerifyPatchForTests("anomaly", target, patch, HarmonyPatchType.Postfix);
        info.Prefixes.Add(new Patch("anomaly", patch));
        Core.VerifyPatchForTests("anomaly", target, patch, HarmonyPatchType.Prefix);
    }

    private bool Send(string content, bool bytes)
    {
        bool result;
        bool runOriginal = bytes
            ? OptionalByteArrayClientSendPatch.OnPackBytes(_hub.encryptedChannelManager, out result,
                new Il2CppStructArray<byte>(Encoding.UTF8.GetBytes(content)), EncryptedChannel.GameConsole)
            : PlayerAuthenticationManagerPatch.OnPack(_hub.encryptedChannelManager, out result, content, EncryptedChannel.GameConsole);
        Assert.False(runOriginal);
        return result;
    }

    private static void AssertPacket(int index, uint counter, string content)
    {
        byte[] packet = AuthTransportCrypto.Decrypt(AnomalyMessaging.Key, AnomalyMessaging.SessionId,
            AnomalyMessaging.Transcript, AuthTransportDirection.ClientToServer, EncryptedMessageTransport.Attempts[index]);
        Assert.Equal((byte)EncryptedChannel.GameConsole, packet[0]);
        Assert.Equal(counter, BitConverter.ToUInt32(packet, 1));
        Assert.Equal(content, Encoding.UTF8.GetString(packet, 5, packet.Length - 5));
    }

    private static byte[] Envelope(uint counter, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        var packet = new byte[bytes.Length + 5];
        packet[0] = (byte)EncryptedChannel.GameConsole;
        BitConverter.GetBytes(counter).CopyTo(packet, 1);
        bytes.CopyTo(packet, 5);
        return AuthTransportCrypto.Encrypt(AnomalyMessaging.Key, AnomalyMessaging.SessionId,
            AnomalyMessaging.Transcript, AuthTransportDirection.ServerToClient, packet);
    }

    private static void Receive(byte[] envelope) => Assert.False(PlayerAuthenticationManagerPatch.OnReceivePackedMessage(
        new EncryptedMessageOutside(envelope, SecurityLevel.EncryptedAndAuthenticated)));
    private static MethodInfo Method(string name) => typeof(EncryptedChannelPatchTests).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
    private static void Target() { }
    private static void ExpectedPatch() { }
    private static void OtherPatch() { }
}
