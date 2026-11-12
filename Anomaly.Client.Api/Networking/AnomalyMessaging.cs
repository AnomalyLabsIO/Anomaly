using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal.Wrappers;
using Anomaly.Client.Api.Modding;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Il2Cpp;
using Il2CppMirror;
using Il2CppMirror.LiteNetLib4Mirror;
using MelonLoader;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Networking;

public static class AnomalyMessaging
{
    internal static Action<Anomaly.Shared.Networking.Messages.FacilitySnapshotMessage> FacilitySnapshotReceived;
    internal static Action<IAnomalyMessage> ContentBootstrapMessageReceived;
    internal static Action TransportSessionRevoked;
    internal static Func<ScriptToClientMessage, bool> ScriptMessageReceived;
    internal static Func<ScriptToServerMessage, bool> ScriptMessageSendAllowed;
    internal static Func<bool> BulkReceiveExpected;
    internal static Func<ulong, bool> BulkSessionExpected;
    internal static Func<int, bool> IsBulkPhysicalChannel;
    [ScriptIgnore]
    public static event Action<IAnomalyMessage> MessageReceived;

    private static readonly object TransportSync = new();
    private static readonly AnomalySequenceState SequenceState = new();
    private static byte[] _clientToServerKey;
    private static byte[] _serverToClientKey;
    private static byte[] _sessionId;
    private static byte[] _transcriptHash;
    private static uint _currentRoundEpoch;
    private static long _transportGeneration;

    // Native connection pointers can survive a rekey. Pending work must also
    // belong to the exact local lifetime of the directional crypto material.
    internal static long TransportGeneration
    {
        get { lock (TransportSync) return _transportGeneration; }
    }

    public static void Send(IAnomalyMessage msg)
    {
        if (!TrySend(msg))
            throw new InvalidOperationException(
                $"Anomaly message '{msg?.MessageName ?? "<null>"}' could not be delivered.");
    }

    /// <summary>
    /// Gets current capacity for a registered outgoing message contract. Does not serialize,
    /// send, advance replay counters, or change the authenticated session. The result measures
    /// only bytes written by the message serializer; delivery rechecks the transport.
    /// </summary>
    public static bool TryGetPayloadBudget(IAnomalyMessage contract, out MessagePayloadBudget budget)
    {
        budget = default;
        try
        {
            var hub = LocalPlayer.Instance?.ReferenceHub;
            if (hub == null && !PlayerRegistry.TryGetLocalHub(out hub))
                return false;
            if ((!NetworkServer.active && !HasTransportSession()) ||
                !AnomalyMessageRegistry.TryGetSendContract(contract, AnomalyMessageDirection.ClientToServer,
                    out _, out var channel, out var policy) ||
                !TryGetPacketLimits(channel, out int maximum, out int singlePacket))
                return false;

            return TransportPacketPolicy.TryGetPayloadBudget(maximum, singlePacket, policy.MaxPayloadBytes,
                !NetworkServer.active, out budget);
        }
        catch { return false; }
    }

    internal static bool TrySend(IAnomalyMessage msg)
    {
        var hub = LocalPlayer.Instance?.ReferenceHub;
        if (hub == null && !PlayerRegistry.TryGetLocalHub(out hub) &&
            !(NetworkClient.connection != null && HasTransportSession() && ContentBootstrapPolicy.IsClientMessage(msg)))
        {
            SafeLogWarning("AnomalyMessaging.Send: No local player hub available.");
            return false;
        }

        bool authenticatedTransportRequired = !NetworkServer.active && HasTransportSession();
        if (!NetworkServer.active && !authenticatedTransportRequired)
        {
            SafeLogWarning(
                "AnomalyMessaging.Send: No authenticated session is active; treating the send as pre-connect API misuse.");
            return false;
        }

        if (!AnomalyMessageRegistry.TryGetSendContract(
                msg, AnomalyMessageDirection.ClientToServer, out uint hash, out var channel, out var policy))
        {
            SafeLogWarning("AnomalyMessaging.Send: Message did not match a registered client-to-server contract.");
            return false;
        }

        if (msg is ScriptToServerMessage scriptMessage && ScriptMessageSendAllowed?.Invoke(scriptMessage) != true)
            return false;
        if (msg is ScriptToServerMessage && !HasScriptQueueCapacity()) return false;

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
            return false;
        }

        if (msg is ScriptToServerMessage && !HasScriptQueueCapacity()) return false;

        // Build the full envelope: [0xFE][4:counter][inner]
        uint counter = AnomalyReplayTable.Instance.NextTxCounter(channel);
        if (counter == 0 || counter == uint.MaxValue)
        {
            FailClosedTransport("Replay counter exhausted before reuse.");
            return false;
        }
        var data = new byte[5 + inner.Length];
        data[0] = AnomalyChannel.ChannelId;
        BitConverter.GetBytes(counter).CopyTo(data, 1);
        Array.Copy(inner, 0, data, 5, inner.Length);

        if (!FitsDeployedTransport(
                channel,
                data.Length + (NetworkServer.active ? 0 : AuthTransportCrypto.EnvelopeOverhead),
                out string transportFailure))
        {
            if (authenticatedTransportRequired)
                FailClosedTransport("The required custom-message transport preflight failed: " + transportFailure);
            SafeLogWarning(transportFailure);
            return false;
        }

        if (NetworkServer.active)
        {
            return EncryptedMessageTransport.Send(
                EncryptedChannelManager.SecurityLevel.Unsecured,
                data,
                (int)channel);
        }

        byte[] encrypted;
        try
        {
            if (!TryEncryptClientEnvelope(data, out encrypted))
            {
                FailClosedTransport(
                    "The custom-message send path lost its directional authenticated transport session.");
                SafeLogWarning(
                    "AnomalyMessaging.Send: No authenticated directional transport session is available.");
                return false;
            }
        }
        catch (Exception exception)
        {
            FailClosedTransport("The required custom-message encryption path threw.");
            SafeLogWarning(
                $"AnomalyMessaging.Send: Authenticated encryption threw: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
        try
        {
            if (EncryptedMessageTransport.Send(
                    EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated,
                    encrypted,
                    (int)channel))
                return true;
        }
        catch (Exception exception)
        {
            FailClosedTransport("The required custom-message delivery path threw.");
            SafeLogWarning(
                $"AnomalyMessaging.Send: Authenticated delivery threw: {exception.GetType().Name}: {exception.Message}");
            return false;
        }

        FailClosedTransport("The required custom-message delivery path rejected the packet.");
        SafeLogWarning("AnomalyMessaging.Send: Authenticated delivery returned false.");
        return false;
    }

    internal static void OnReceived(
        byte[] anomalyPayload,
        bool encryptedAndAuthenticated,
        bool localTransport,
        bool beforeContentReady = false)
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
        bool bulkChunk = AnomalyMessageRegistry.IsRegisteredType<FileChunkMessage>(hash);
        byte bulkDomain = 0;
        ulong bulkSessionId = 0;
        if (bulkChunk && !BulkTransferProtocol.TryReadChunkDomain(anomalyPayload, 8, payloadBytes, out bulkDomain))
        {
            RegisterProtocolViolation("File chunk had an invalid authenticated bulk prefix.", localTransport);
            return;
        }
        if (bulkChunk && !BulkTransferProtocol.TryReadChunkSession(anomalyPayload, 8, payloadBytes, out bulkSessionId))
        {
            RegisterProtocolViolation("File chunk had an invalid authenticated session prefix.", localTransport);
            return;
        }
        if (bulkChunk && BulkSessionExpected?.Invoke(bulkSessionId) != true)
        {
            // Raw admission already charged this authenticated envelope before
            // decryption. Retired content must neither spend ordinary semantic
            // allowance nor advance the crypto-session replay window.
            if (!AnomalyMessageRegistry.TryGetPolicy(hash, out var retiredPolicy) || payloadBytes > retiredPolicy.MaxPayloadBytes)
                RegisterProtocolViolation("Retired file chunk exceeded its built-in payload contract.", localTransport);
            return;
        }

        // These registered built-ins are optional, authenticated server presentation. Keep
        // their bounded allowance separate so normal traffic cannot turn cosmetic congestion
        // into a disconnect. Invalid counters and oversized contracts keep the strict path.
        bool poseSample = AnomalyMessageRegistry.IsRegisteredType<PlayerPoseSampleMessage>(hash) ||
            AnomalyMessageRegistry.IsRegisteredType<PlayerCombatPoseSampleMessage>(hash);
        bool optionalPose = encryptedAndAuthenticated && !beforeContentReady && counter != 0 && counter != uint.MaxValue &&
            (poseSample || AnomalyMessageRegistry.IsRegisteredType<PlayerPoseControlMessage>(hash) ||
             AnomalyMessageRegistry.IsRegisteredType<PlayerCombatPoseControlMessage>(hash)) &&
            AnomalyMessageRegistry.TryGetPolicy(hash, out var posePolicy) &&
            posePolicy.Direction == AnomalyMessageDirection.ServerToClient && payloadBytes <= posePolicy.MaxPayloadBytes;
        var rateResult = bulkChunk && BulkReceiveExpected?.Invoke() == true
            ? AnomalyReplayTable.BulkInboundRateLimiter.TryAccept(hash, payloadBytes)
            : optionalPose
            ? AnomalyReplayTable.PoseInboundRateLimiter.TryAcceptOptional(hash, payloadBytes)
            : AnomalyReplayTable.InboundRateLimiter.TryAccept(hash, payloadBytes);
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

        bool shouldLog;
        var rx = bulkChunk
            ? AnomalyReplayTable.Bulk.TryAcceptRx(bulkDomain, counter, out shouldLog)
            : AnomalyReplayTable.Instance.TryAcceptRx(channel, counter, out shouldLog);
        if (rx != AnomalyReplayState.RxResult.Accepted)
        {
            // Unreliable pose samples may legitimately arrive outside the transport replay
            // window or repeat after jitter. They still never reach deserialization/handlers.
            if (optionalPose && poseSample &&
                (rx == AnomalyReplayState.RxResult.RejectedReplay || rx == AnomalyReplayState.RxResult.RejectedStale))
                return;
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

        if (beforeContentReady && !ContentBootstrapPolicy.IsServerMessage(message))
        {
            FailClosedTransport($"Message '{message.MessageName}' arrived before authenticated content readiness.");
            return;
        }

        try
        {
            using var ms = new MemoryStream(anomalyPayload, 8, anomalyPayload.Length - 8);
            using var reader = new BinaryReader(ms);
            message.Deserialize(reader);
            if (message is FileChunkMessage chunk) chunk.FrameCounter = counter;
            if (ms.Position != ms.Length)
                throw new InvalidDataException(
                    $"Message left {ms.Length - ms.Position} unread payload bytes.");
        }
        catch (Exception ex)
        {
            if (ContentBootstrapPolicy.IsServerMessage(message))
            {
                FailClosedTransport("The authenticated connection bootstrap was malformed: " + ex.Message);
                return;
            }
            RegisterProtocolViolation(
                $"AnomalyMessaging.OnReceived: Failed to deserialize '{message.MessageName}': " +
                $"{ex.GetType().Name}: {ex.Message}",
                localTransport);
            return;
        }

        if (message is Anomaly.Shared.Networking.Messages.FacilitySnapshotMessage snapshot)
        {
            try
            {
                var receiver = FacilitySnapshotReceived ??
                    throw new InvalidOperationException("The facility bootstrap receiver is unavailable.");
                receiver(snapshot);
            }
            catch (Exception exception)
            {
                FailClosedTransport("The authenticated facility snapshot could not be applied: " + exception.Message);
            }
            return;
        }
        if (ContentBootstrapPolicy.IsServerMessage(message))
        {
            try
            {
                var receiver = ContentBootstrapMessageReceived ??
                    throw new InvalidOperationException("The content bootstrap receiver is unavailable.");
                receiver(message);
            }
            catch (Exception exception)
            {
                FailClosedTransport("The authenticated content bootstrap failed: " + exception.Message);
            }
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

        if (message is ScriptToClientMessage scriptMessage)
        {
            try
            {
                // The client-owned bridge checks the immutable approved pack and
                // queues Lua before ordinary observers can see this carrier.
                if (ScriptMessageReceived?.Invoke(scriptMessage) != true) return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                FailClosedTransport("The approved script message bridge failed: " + exception.GetType().Name);
                return;
            }
        }

        foreach (Delegate subscriber in MessageReceived?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            if (!localTransport && !HasTransportSession())
                return;
            try { ((Action<IAnomalyMessage>)subscriber)(message); }
            catch (Exception ex)
            {
                SafeLogError($"AnomalyMessaging.OnReceived: Isolated handler exception for '{message.MessageName}': {ex}");
            }
        }
        if (!localTransport && !HasTransportSession())
            return;
        try { NetworkMessageEvents.OnMessageReceived(message); }
        catch (Exception ex) { SafeLogError($"AnomalyMessaging.MessageReceived publication failed: {ex}"); }
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
        if (result.ShouldDisconnect && !localTransport)
            FailClosedTransport("Sustained invalid or over-limit server traffic was rejected.");
        if (result.ShouldLog)
            SafeLogWarning($"{message} (drops={result.DroppedInWindow}).");
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
            throw new ArgumentException("Invalid directional authenticated transport session material.");

        // Erase old keys first: failing cleanup must leave neither the previous
        // session nor a replacement authenticated transport usable.
        lock (TransportSync)
            ClearTransportSessionLocked();
        TransportSessionRevoked?.Invoke();
        lock (TransportSync)
        {
            _clientToServerKey = (byte[])clientToServerKey.Clone();
            _serverToClientKey = (byte[])serverToClientKey.Clone();
            _sessionId = (byte[])sessionId.Clone();
            _transcriptHash = (byte[])transcriptHash.Clone();
        }
    }

    internal static long ClearTransportSession()
    {
        long generation;
        lock (TransportSync)
        {
            ClearTransportSessionLocked();
            generation = _transportGeneration;
        }
        try { TransportSessionRevoked?.Invoke(); }
        catch (Exception exception) { SafeLogError("Connection scope revocation failed: " + exception); }
        return generation;
    }

    private static bool HasTransportSession()
    {
        lock (TransportSync)
        {
            return _clientToServerKey != null &&
                   _serverToClientKey != null &&
                   _sessionId != null &&
                   _transcriptHash != null;
        }
    }

    internal static void FailClosedTransport(string reason)
    {
        Exception clearFailure = null;
        Exception disconnectFailure = null;
        try { ClearTransportSession(); }
        catch (Exception exception) { clearFailure = exception; }
        try { NetworkClient.connection?.Disconnect(); }
        catch (Exception exception) { disconnectFailure = exception; }

        try
        {
            FeatureHealth.Report(
                "anomaly",
                "networking",
                FeatureHealthStatus.Failed,
                $"{reason} Reconnect/rekey is required.");
        }
        catch { }

        SafeLogError($"{reason} Authenticated transport was revoked and disconnect was requested.");
        if (clearFailure != null)
            SafeLogError($"Failed to clear authenticated transport material: {clearFailure}");
        if (disconnectFailure != null)
            SafeLogError($"Failed to disconnect after authenticated transport revocation: {disconnectFailure}");
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
        AnomalyReplayTable.ResetBulk();
        Clear(_clientToServerKey);
        Clear(_serverToClientKey);
        Clear(_sessionId);
        Clear(_transcriptHash);
        _clientToServerKey = null;
        _serverToClientKey = null;
        _sessionId = null;
        _transcriptHash = null;
        // Exhaustion fails closed after erasing material; never wrap and make a
        // prior lifetime current again.
        _transportGeneration = checked(_transportGeneration + 1);
    }

    private static void SafeLogWarning(string message)
    {
        try { MelonLogger.Warning(message); }
        catch { }
    }

    private static void SafeLogError(string message)
    {
        try { MelonLogger.Error(message); }
        catch { }
    }

    private static void Clear(byte[] value)
    {
        if (value != null)
            Array.Clear(value, 0, value.Length);
    }

    private static bool FitsDeployedTransport(
        MessageChannel channel,
        int encodedBytes,
        out string failure)
    {
        failure = null;
        try
        {
            if (!TryGetPacketLimits(channel, out int maximum, out _))
            {
                failure = "AnomalyMessaging.Send: The recipient transport is unavailable.";
                return false;
            }
            bool fits = TransportPacketPolicy.Fits(maximum, encodedBytes);
            if (fits)
                return true;
            int required = encodedBytes > int.MaxValue - TransportPacketPolicy.FramingReserveBytes
                ? int.MaxValue
                : encodedBytes + TransportPacketPolicy.FramingReserveBytes;
            failure =
                $"AnomalyMessaging.Send: encoded packet exceeded deployed channel {channel} " +
                $"capacity ({required}/{maximum} bytes).";
            return false;
        }
        catch (Exception exception)
        {
            failure =
                $"AnomalyMessaging.Send: transport ceiling query failed closed: {exception.Message}";
            return false;
        }
    }

    private static bool HasScriptQueueCapacity()
    {
        if (NetworkServer.active) return true;
        try
        {
            var transport = LiteNetLib4MirrorTransport.Singleton;
            var peer = LiteNetLib4MirrorCore.Host?.FirstPeer;
            if (transport?.channels == null || transport.channels.Length > BulkTransferProtocol.MaxPhysicalChannels || peer == null ||
                peer.ConnectionState != Il2CppLiteNetLib.ConnectionState.Connected) return false;
            long queued = 0;
            for (int index = 0; index < transport.channels.Length; index++)
            {
                if (IsBulkPhysicalChannel?.Invoke(index) == true) continue;
                var delivery = transport.channels[index];
                if (delivery != Il2CppLiteNetLib.DeliveryMethod.ReliableOrdered &&
                    delivery != Il2CppLiteNetLib.DeliveryMethod.ReliableUnordered) continue;
                int count = peer.GetPacketsCountInReliableQueue((byte)index,
                    delivery == Il2CppLiteNetLib.DeliveryMethod.ReliableOrdered);
                if (count < 0 || (queued += count) >= 128) return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        { return false; }
    }

    private static bool TryGetPacketLimits(MessageChannel channel, out int maximumPacketBytes, out int singlePacketBytes)
    {
        maximumPacketBytes = singlePacketBytes = 0;
        var transport = LiteNetLib4MirrorTransport.Singleton;
        int channelId = (int)channel;
        if (transport == null || transport.channels == null || channelId < 0 || channelId >= transport.channels.Length)
            return false;

        if (NetworkServer.active)
        {
            maximumPacketBytes = singlePacketBytes = transport.GetMaxPacketSize(channelId);
            return maximumPacketBytes > 0;
        }

        // A client has one remote peer. Server budgets instead resolve the target connection.
        var peer = LiteNetLib4MirrorCore.Host?.FirstPeer;
        if (peer == null || peer.ConnectionState != Il2CppLiteNetLib.ConnectionState.Connected)
            return false;
        var delivery = transport.channels[channelId];
        singlePacketBytes = peer.GetMaxSinglePacketSize(delivery);
        maximumPacketBytes = TransportPacketPolicy.GetMaximumPacketBytes(singlePacketBytes,
            delivery == Il2CppLiteNetLib.DeliveryMethod.ReliableOrdered ||
            delivery == Il2CppLiteNetLib.DeliveryMethod.ReliableUnordered);
        maximumPacketBytes = Math.Min(maximumPacketBytes, transport.GetMaxPacketSize(channelId));
        singlePacketBytes = Math.Min(singlePacketBytes, maximumPacketBytes);
        return maximumPacketBytes > 0;
    }
}
