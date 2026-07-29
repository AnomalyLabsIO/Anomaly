using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal.Wrappers;
using Anomaly.Shared.Networking;
using Il2Cpp;
using Il2CppMirror;
using MelonLoader;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Anomaly.Client.Api.Networking;

public static class AnomalyMessaging
{
    public static event Action<IAnomalyMessage> MessageReceived;

    private static readonly SecureRandom SecureRandom = new();
    private static readonly AnomalySequenceState SequenceState = new();
    private static uint _currentRoundEpoch;

    public static void Send(IAnomalyMessage msg)
    {
        var hub = LocalPlayer.Instance?.ReferenceHub;
        if (hub == null && !PlayerRegistry.TryGetLocalHub(out hub))
        {
            MelonLogger.Warning("AnomalyMessaging.Send: No local player hub available.");
            return;
        }

        var ecm = hub.encryptedChannelManager;
        if (ecm.EncryptionKey == null)
        {
            MelonLogger.Warning("AnomalyMessaging.Send: No encryption key available.");
            return;
        }

        uint counter = AnomalyReplayTable.Instance.NextTxCounter(msg.TransportChannel);
        uint hash = AnomalyMessageRegistry.GetHash(msg.MessageName);

        if (!AnomalyMessageRegistry.TryGetPolicy(hash, out var policy))
        {
            MelonLogger.Warning($"AnomalyMessaging.Send: No policy registered for '{msg.MessageName}'.");
            return;
        }
        if (policy.Direction == AnomalyMessageDirection.ServerToClient)
        {
            MelonLogger.Warning($"AnomalyMessaging.Send: '{msg.MessageName}' is server-to-client only.");
            return;
        }

        // Serialize the inner payload: [hash][message payload]
        using var innerMs = new MemoryStream();
        using var innerWriter = new BinaryWriter(innerMs);
        innerWriter.Write(hash);
        msg.Serialize(innerWriter);
        innerWriter.Flush();
        var inner = innerMs.ToArray();
        int serializedPayloadBytes = inner.Length - sizeof(uint);
        if (serializedPayloadBytes > policy.MaxPayloadBytes ||
            inner.Length + 5 > AnomalyChannel.MaxEnvelopeBytes)
        {
            MelonLogger.Warning(
                $"AnomalyMessaging.Send: '{msg.MessageName}' exceeded its payload ceiling " +
                $"({serializedPayloadBytes}/{policy.MaxPayloadBytes} bytes).");
            return;
        }

        // Build the full envelope: [0xFE][4:counter][inner]
        var data = new byte[5 + inner.Length];
        data[0] = AnomalyChannel.ChannelId;
        BitConverter.GetBytes(counter).CopyTo(data, 1);
        Array.Copy(inner, 0, data, 5, inner.Length);

        if (NetworkServer.active)
        {
            EncryptedMessageTransport.Send(
                EncryptedChannelManager.SecurityLevel.Unsecured,
                data,
                (int)msg.TransportChannel);
        }
        else
        {
            var encrypted = AesGcmEncrypt(data, ecm.EncryptionKey);
            EncryptedMessageTransport.Send(
                EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated,
                encrypted,
                (int)msg.TransportChannel);
        }
    }

    internal static void OnReceived(
        byte[] anomalyPayload,
        bool encryptedAndAuthenticated,
        bool localTransport)
    {
        if (!encryptedAndAuthenticated && !localTransport)
        {
            MelonLogger.Warning("AnomalyMessaging.OnReceived: Dropping unauthenticated remote 0xFE message.");
            return;
        }
        if (anomalyPayload == null || anomalyPayload.Length > AnomalyChannel.MaxEnvelopeBytes)
        {
            MelonLogger.Warning(
                $"AnomalyMessaging.OnReceived: Payload exceeded the absolute envelope ceiling " +
                $"({anomalyPayload?.Length ?? 0}/{AnomalyChannel.MaxEnvelopeBytes} bytes).");
            return;
        }

        // Wire (after stripping 0xFE): [4:counter][4:hash][N:payload]
        if (anomalyPayload.Length < 8)
        {
            MelonLogger.Warning("AnomalyMessaging.OnReceived: Payload too short for counter + message hash.");
            return;
        }

        uint counter = BitConverter.ToUInt32(anomalyPayload, 0);
        uint hash = BitConverter.ToUInt32(anomalyPayload, 4);

        if (!AnomalyMessageRegistry.TryGetChannel(hash, out var channel))
        {
            MelonLogger.Warning($"AnomalyMessaging.OnReceived: Unknown message hash 0x{hash:X8}.");
            return;
        }

        if (!AnomalyMessageRegistry.TryGetPolicy(hash, out var policy))
        {
            MelonLogger.Warning($"AnomalyMessaging.OnReceived: No policy for hash 0x{hash:X8}.");
            return;
        }
        if (policy.Direction == AnomalyMessageDirection.ClientToServer)
        {
            MelonLogger.Warning($"AnomalyMessaging.OnReceived: Dropping client-to-server message hash 0x{hash:X8}.");
            return;
        }

        int payloadBytes = anomalyPayload.Length - 8;
        if (payloadBytes > policy.MaxPayloadBytes)
        {
            MelonLogger.Warning(
                $"AnomalyMessaging.OnReceived: Payload for hash 0x{hash:X8} exceeded its ceiling " +
                $"({payloadBytes}/{policy.MaxPayloadBytes} bytes).");
            return;
        }

        var rx = AnomalyReplayTable.Instance.TryAcceptRx(channel, counter, out bool shouldLog);
        if (rx != AnomalyReplayState.RxResult.Accepted)
        {
            if (shouldLog)
                MelonLogger.Warning($"AnomalyMessaging.OnReceived: Dropping packet (hash=0x{hash:X8}, counter={counter}, channel={channel}, reason={rx}).");
            return;
        }

        if (!AnomalyMessageRegistry.TryCreate(hash, out var message))
        {
            MelonLogger.Warning($"AnomalyMessaging.OnReceived: No factory for hash 0x{hash:X8}.");
            return;
        }

        try
        {
            using var ms = new MemoryStream(anomalyPayload, 8, anomalyPayload.Length - 8);
            using var reader = new BinaryReader(ms);
            message.Deserialize(reader);
            if (ms.Position != ms.Length)
                throw new InvalidDataException(
                    $"Message left {ms.Length - ms.Position} unread payload bytes.");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"AnomalyMessaging.OnReceived: Failed to deserialize '{message.MessageName}': {ex}");
            return;
        }

        if (policy.IsRoundScoped)
        {
            if (message is not IRoundScopedAnomalyMessage scoped)
            {
                MelonLogger.Warning($"AnomalyMessaging.OnReceived: Round-scoped policy mismatch for hash 0x{hash:X8}.");
                return;
            }

            var result = SequenceState.TryAccept(
                _currentRoundEpoch,
                scoped.RoundEpoch,
                scoped.Sequence,
                hash);
            if (result != AnomalySequenceState.RxResult.Accepted)
            {
                MelonLogger.Warning(
                    $"AnomalyMessaging.OnReceived: Dropping round-scoped packet " +
                    $"(hash=0x{hash:X8}, epoch={scoped.RoundEpoch}/{_currentRoundEpoch}, " +
                    $"sequence={scoped.Sequence}, reason={result}).");
                return;
            }
        }

        try
        {
            MessageReceived?.Invoke(message);
            NetworkMessageEvents.OnMessageReceived(message);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"AnomalyMessaging.OnReceived: Handler exception for '{message.MessageName}': {ex}");
        }
    }

    internal static void ResetSession(uint roundEpoch)
    {
        _currentRoundEpoch = roundEpoch;
        SequenceState.Reset(roundEpoch);
    }

    private static byte[] AesGcmEncrypt(byte[] data, byte[] key)
    {
        byte[] nonce = new byte[32];
        SecureRandom.NextBytes(nonce, 0, nonce.Length);

        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(true, new AeadParameters(new KeyParameter(key), 128, nonce));

        int outputSize = cipher.GetOutputSize(data.Length);
        var ciphertext = new byte[outputSize];
        int len = cipher.ProcessBytes(data, 0, data.Length, ciphertext, 0);
        cipher.DoFinal(ciphertext, len);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(nonce);
        writer.Write(ciphertext, 0, outputSize);
        return ms.ToArray();
    }
}
