using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal.Wrappers;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Il2Cpp;
using Il2CppMirror;
using MelonLoader;

namespace Anomaly.Client.Api.Networking;

public static class AnomalyMessaging
{
    public static event Action<IAnomalyMessage> MessageReceived;

    private static readonly object TransportSync = new();
    private static readonly AnomalySequenceState SequenceState = new();
    private static byte[] _clientToServerKey;
    private static byte[] _serverToClientKey;
    private static byte[] _sessionId;
    private static byte[] _transcriptHash;
    private static uint _currentRoundEpoch;

    public static void Send(IAnomalyMessage msg)
    {
        var hub = LocalPlayer.Instance?.ReferenceHub;
        if (hub == null && !PlayerRegistry.TryGetLocalHub(out hub))
        {
            MelonLogger.Warning("AnomalyMessaging.Send: No local player hub available.");
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
            if (!TryEncryptClientEnvelope(data, out var encrypted))
            {
                MelonLogger.Warning("AnomalyMessaging.Send: No authenticated protocol-v2 transport session is available.");
                return;
            }
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
            RegisterProtocolViolation(
                "AnomalyMessaging.OnReceived: Dropping unauthenticated remote 0xFE message",
                localTransport);
            return;
        }
        if (anomalyPayload == null || anomalyPayload.Length > AnomalyChannel.MaxEnvelopeBytes)
        {
            RegisterProtocolViolation(
                $"AnomalyMessaging.OnReceived: Payload exceeded the absolute envelope ceiling " +
                $"({anomalyPayload?.Length ?? 0}/{AnomalyChannel.MaxEnvelopeBytes} bytes)",
                localTransport);
            return;
        }

        // Wire (after stripping 0xFE): [4:counter][4:hash][N:payload]
        if (anomalyPayload.Length < 8)
        {
            RegisterProtocolViolation(
                "AnomalyMessaging.OnReceived: Payload too short for counter + message hash",
                localTransport);
            return;
        }

        uint counter = BitConverter.ToUInt32(anomalyPayload, 0);
        uint hash = BitConverter.ToUInt32(anomalyPayload, 4);
        int payloadBytes = anomalyPayload.Length - 8;

        var rateResult = AnomalyReplayTable.InboundRateLimiter.TryAccept(hash, payloadBytes);
        if (!rateResult.Allowed)
        {
            HandleRateLimitResult(
                rateResult,
                $"AnomalyMessaging.OnReceived: Rate-limited hash 0x{hash:X8}",
                localTransport);
            return;
        }

        if (!AnomalyMessageRegistry.TryGetChannel(hash, out var channel))
        {
            RegisterProtocolViolation(
                $"AnomalyMessaging.OnReceived: Unknown message hash 0x{hash:X8}",
                localTransport);
            return;
        }

        if (!AnomalyMessageRegistry.TryGetPolicy(hash, out var policy))
        {
            RegisterProtocolViolation(
                $"AnomalyMessaging.OnReceived: No policy for hash 0x{hash:X8}",
                localTransport);
            return;
        }
        if (policy.AuthPhase == AnomalyMessageAuthPhase.AuthenticatedSession &&
            !encryptedAndAuthenticated &&
            !localTransport)
        {
            RegisterProtocolViolation(
                $"AnomalyMessaging.OnReceived: Dropping pre-auth message hash 0x{hash:X8}",
                localTransport);
            return;
        }
        if (policy.Direction == AnomalyMessageDirection.ClientToServer)
        {
            RegisterProtocolViolation(
                $"AnomalyMessaging.OnReceived: Dropping client-to-server message hash 0x{hash:X8}",
                localTransport);
            return;
        }

        if (payloadBytes > policy.MaxPayloadBytes)
        {
            RegisterProtocolViolation(
                $"AnomalyMessaging.OnReceived: Payload for hash 0x{hash:X8} exceeded its ceiling " +
                $"({payloadBytes}/{policy.MaxPayloadBytes} bytes)",
                localTransport);
            return;
        }

        var rx = AnomalyReplayTable.Instance.TryAcceptRx(channel, counter, out bool shouldLog);
        if (rx != AnomalyReplayState.RxResult.Accepted)
        {
            if (shouldLog)
            {
                RegisterProtocolViolation(
                    $"AnomalyMessaging.OnReceived: Dropping packet " +
                    $"(hash=0x{hash:X8}, counter={counter}, channel={channel}, reason={rx})",
                    localTransport);
            }
            else
            {
                var violation = AnomalyReplayTable.InboundRateLimiter.RegisterProtocolViolation();
                if (violation.ShouldDisconnect)
                    HandleRateLimitResult(violation, "AnomalyMessaging.OnReceived: Sustained replay traffic", localTransport);
            }
            return;
        }

        if (!AnomalyMessageRegistry.TryCreate(hash, out var message))
        {
            RegisterProtocolViolation(
                $"AnomalyMessaging.OnReceived: No factory for hash 0x{hash:X8}",
                localTransport);
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
            RegisterProtocolViolation(
                $"AnomalyMessaging.OnReceived: Failed to deserialize '{message.MessageName}': " +
                $"{ex.GetType().Name}: {ex.Message}",
                localTransport);
            return;
        }

        if (policy.IsRoundScoped)
        {
            if (message is not IRoundScopedAnomalyMessage scoped)
            {
                RegisterProtocolViolation(
                    $"AnomalyMessaging.OnReceived: Round-scoped policy mismatch for hash 0x{hash:X8}",
                    localTransport);
                return;
            }

            var result = SequenceState.TryAccept(
                _currentRoundEpoch,
                scoped.RoundEpoch,
                scoped.Sequence,
                hash);
            if (result != AnomalySequenceState.RxResult.Accepted)
            {
                RegisterProtocolViolation(
                    $"AnomalyMessaging.OnReceived: Dropping round-scoped packet " +
                    $"(hash=0x{hash:X8}, epoch={scoped.RoundEpoch}/{_currentRoundEpoch}, " +
                    $"sequence={scoped.Sequence}, reason={result})",
                    localTransport);
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

    private static void RegisterProtocolViolation(string message, bool localTransport)
    {
        var result = AnomalyReplayTable.InboundRateLimiter.RegisterProtocolViolation();
        HandleRateLimitResult(result, message, localTransport);
    }

    private static void HandleRateLimitResult(
        AnomalyRateLimitResult result,
        string message,
        bool localTransport)
    {
        if (result.ShouldLog)
            MelonLogger.Warning($"{message} (drops={result.DroppedInWindow}).");
        if (!result.ShouldDisconnect || localTransport)
            return;

        MelonLogger.Error(
            "AnomalyMessaging.OnReceived: Disconnecting after sustained invalid or over-limit server traffic.");
        try
        {
            NetworkClient.connection?.Disconnect();
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"AnomalyMessaging.OnReceived: Rate-limit disconnect failed: {ex.Message}");
        }
    }

    internal static void ConfigureTransportSession(
        byte[] clientToServerKey,
        byte[] serverToClientKey,
        byte[] sessionId,
        byte[] transcriptHash)
    {
        if (clientToServerKey?.Length != 32 || serverToClientKey?.Length != 32 ||
            sessionId?.Length != AuthTransportCrypto.SessionIdSize ||
            transcriptHash?.Length != AuthTransportCrypto.TranscriptHashSize)
            throw new ArgumentException("Invalid protocol-v2 transport session material.");

        lock (TransportSync)
        {
            ClearTransportSessionLocked();
            _clientToServerKey = (byte[])clientToServerKey.Clone();
            _serverToClientKey = (byte[])serverToClientKey.Clone();
            _sessionId = (byte[])sessionId.Clone();
            _transcriptHash = (byte[])transcriptHash.Clone();
        }
    }

    internal static void ClearTransportSession()
    {
        lock (TransportSync)
            ClearTransportSessionLocked();
    }

    internal static bool TryEncryptClientEnvelope(byte[] plaintext, out byte[] envelope)
    {
        lock (TransportSync)
        {
            if (_clientToServerKey == null)
            {
                envelope = null;
                return false;
            }

            envelope = AuthTransportCrypto.Encrypt(
                _clientToServerKey,
                _sessionId,
                _transcriptHash,
                AuthTransportDirection.ClientToServer,
                plaintext);
            return true;
        }
    }

    internal static bool TryDecryptServerEnvelope(byte[] envelope, out byte[] plaintext)
    {
        lock (TransportSync)
        {
            if (_serverToClientKey == null)
            {
                plaintext = null;
                return false;
            }

            plaintext = AuthTransportCrypto.Decrypt(
                _serverToClientKey,
                _sessionId,
                _transcriptHash,
                AuthTransportDirection.ServerToClient,
                envelope);
            return true;
        }
    }

    private static void ClearTransportSessionLocked()
    {
        Clear(_clientToServerKey);
        Clear(_serverToClientKey);
        Clear(_sessionId);
        Clear(_transcriptHash);
        _clientToServerKey = null;
        _serverToClientKey = null;
        _sessionId = null;
        _transcriptHash = null;
    }

    private static void Clear(byte[] value)
    {
        if (value != null)
            Array.Clear(value, 0, value.Length);
    }
}
