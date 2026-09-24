using System;
using System.IO;
using System.Text;
using Anomaly.Shared.Networking;
using GameCore;
using Mirror;
using Mirror.LiteNetLib4Mirror;
using NetworkManagerUtils.Dummies;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Networking;

internal static class AnomalyMessaging
{
    public static event Action<ReferenceHub, IAnomalyMessage> MessageReceived;

    /// <summary>
    /// Fires once per newly-authenticated hub the moment the Anomaly encrypted
    /// channel is ready to carry messages to that client (i.e. its directional
    /// directional authenticated transport session has been registered).
    /// Subsystems that need to push per-player state immediately after auth —
    /// before the round-start snapshot goes out — hook this.
    /// </summary>
    public static event Action<ReferenceHub> ClientReady;

    internal static void RaiseClientReady(ReferenceHub hub)
    {
        foreach (Delegate subscriber in ClientReady?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            if (!Plugin.IsAuthenticatedAdmissionCurrent(hub))
                return;
            try { ((Action<ReferenceHub>)subscriber)(hub); }
            catch (Exception ex) { SafeLogError($"AnomalyMessaging.ClientReady subscriber threw: {ex}"); }
        }
        if (Plugin.IsAuthenticatedAdmissionCurrent(hub))
        {
            try { Anomaly.Server.Api.Events.Handlers.ClientEvents.OnConnected(hub); }
            catch (Exception ex) { SafeLogError($"AnomalyMessaging.Connected publication failed: {ex}"); }
        }
    }

    internal static uint CurrentRoundEpoch { get; set; }

    /// <summary>
    /// Whether <paramref name="hub"/> is a real client we can deliver Anomaly
    /// messages to. Filters out dummies (whose <see cref="DummyNetworkConnection"/>
    /// is a no-op) and hubs without a backing connection. Encryption-key
    /// readiness is checked at send time inside <see cref="SendToClient"/>.
    /// </summary>
    internal static bool IsAnomalyTarget(ReferenceHub hub)
    {
        if (hub == null) return false;
        if (hub.IsDummy) return false;
        var conn = hub.connectionToClient;
        if (conn == null) return AuthenticatedTransportSessions.IsActive(hub);
        if (conn is DummyNetworkConnection) return false;
        return true;
    }

    public static void SendToClient(ReferenceHub hub, IAnomalyMessage msg)
    {
        if (!TrySendToClient(hub, msg))
            throw new InvalidOperationException(
                $"Anomaly message '{msg?.MessageName ?? "<null>"}' could not be delivered.");
    }

    internal static bool TrySendToClient(ReferenceHub hub, IAnomalyMessage msg)
    {
        if (!IsAnomalyTarget(hub)) return false;

        bool localTransport = hub.encryptedChannelManager.isLocalPlayer;
        bool authenticatedTransportRequired =
            !localTransport && AuthenticatedTransportSessions.IsActive(hub);
        if (!localTransport && !authenticatedTransportRequired)
        {
            SafeLogWarning(
                "AnomalyMessaging.SendToClient: No authenticated session is active; " +
                "treating the send as pre-admission API misuse.");
            return false;
        }
        if (hub.connectionToClient == null)
        {
            FailClosedDisconnect(
                hub,
                "AnomalyMessaging.SendToClient: Authenticated connection disappeared before delivery.");
            return false;
        }

        if (!AnomalyMessageRegistry.TryGetSendContract(
                msg, AnomalyMessageDirection.ServerToClient, out uint hash, out var channel, out var policy))
        {
            SafeLogWarning("AnomalyMessaging.SendToClient: Message did not match a registered server-to-client contract.");
            return false;
        }

        var data = BuildPayload(msg, 0, hash, policy);
        if (data == null) return false;
        var state = AnomalyReplayTable.GetOrCreate(hub);
        uint counter = state.NextTxCounter(channel);
        if (counter == 0 || counter == uint.MaxValue)
        {
            FailClosedDisconnect(
                hub,
                "AnomalyMessaging.SendToClient: Counter exhausted; disconnecting before counter reuse.");
            return false;
        }
        BitConverter.GetBytes(counter).CopyTo(data, 1);

        EncryptedChannelManager.EncryptedMessageOutside packed;
        if (localTransport)
        {
            packed = new EncryptedChannelManager.EncryptedMessageOutside(
                EncryptedChannelManager.SecurityLevel.Unsecured, data);
        }
        else
        {
            byte[] encrypted;
            try
            {
                if (!AuthenticatedTransportSessions.TryEncryptToClient(hub, data, out encrypted))
                {
                    FailClosedDisconnect(
                        hub,
                        "AnomalyMessaging.SendToClient: Directional authenticated transport disappeared before encryption.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                FailClosedDisconnect(
                    hub,
                    "AnomalyMessaging.SendToClient: Required authenticated encryption failed.");
                SafeLogWarning(
                    $"AnomalyMessaging.SendToClient: Encryption failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            packed = new EncryptedChannelManager.EncryptedMessageOutside(
                EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated, encrypted);
        }

        if (!FitsDeployedTransport(
                channel,
                packed.Data?.Length ?? 0,
                out string transportFailure))
        {
            if (authenticatedTransportRequired)
            {
                FailClosedDisconnect(
                    hub,
                    "AnomalyMessaging.SendToClient: Required payload did not fit the deployed transport.");
            }
            SafeLogWarning(transportFailure);
            return false;
        }
        try
        {
            hub.connectionToClient.Send(packed, (int)channel);
        }
        catch (Exception exception)
        {
            if (authenticatedTransportRequired)
            {
                FailClosedDisconnect(
                    hub,
                    "AnomalyMessaging.SendToClient: Required authenticated delivery threw.");
            }
            SafeLogWarning(
                $"AnomalyMessaging.SendToClient: Delivery threw: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
        return true;
    }

    public static void SendToAll(IAnomalyMessage msg)
    {
        foreach (var hub in ReferenceHub.AllHubs)
        {
            try
            {
                if (!IsAnomalyTarget(hub))
                    continue;
                SendToClient(hub, msg);
            }
            catch (Exception exception)
            {
                SafeLogError(
                    $"AnomalyMessaging.SendToAll isolated failure for {DescribeHub(hub)}: {exception}");
            }
        }
    }

    public static void SendToAllExcept(NetworkConnection exclude, IAnomalyMessage msg)
    {
        foreach (var hub in ReferenceHub.AllHubs)
        {
            try
            {
                if (!IsAnomalyTarget(hub))
                    continue;
                if (hub.connectionToClient == exclude)
                    continue;
                SendToClient(hub, msg);
            }
            catch (Exception exception)
            {
                SafeLogError(
                    $"AnomalyMessaging.SendToAllExcept isolated failure for {DescribeHub(hub)}: {exception}");
            }
        }
    }

    internal static void OnReceived(
        ReferenceHub sender,
        byte[] anomalyPayload,
        bool encryptedAndAuthenticated,
        bool localTransport)
    {
        if (!encryptedAndAuthenticated && !localTransport)
        {
            RegisterProtocolViolation(
                sender,
                $"AnomalyMessaging.OnReceived: Dropping unauthenticated remote 0xFE message from " +
                $"{sender?.LoggedNameFromRefHub() ?? "unknown"}",
                localTransport);
            return;
        }
        if (anomalyPayload == null || anomalyPayload.Length > AnomalyChannel.MaxEnvelopeBytes)
        {
            RegisterProtocolViolation(
                sender,
                $"AnomalyMessaging.OnReceived: Payload exceeded the absolute envelope ceiling " +
                $"({anomalyPayload?.Length ?? 0}/{AnomalyChannel.MaxEnvelopeBytes} bytes)",
                localTransport);
            return;
        }

        // Wire: [4:counter][4:hash][N:payload]
        if (anomalyPayload.Length < 8)
        {
            RegisterProtocolViolation(
                sender,
                "AnomalyMessaging.OnReceived: Payload too short for counter + message hash",
                localTransport);
            return;
        }

        uint counter = BitConverter.ToUInt32(anomalyPayload, 0);
        uint hash = BitConverter.ToUInt32(anomalyPayload, 4);
        int payloadBytes = anomalyPayload.Length - 8;

        if (sender == null)
        {
            Logger.Warn("AnomalyMessaging.OnReceived: Dropping message with no sender hub.");
            return;
        }

        if (counter == uint.MaxValue)
        {
            FailClosedDisconnect(
                sender,
                "AnomalyMessaging.OnReceived: Counter exhausted; disconnecting before wrap.");
            return;
        }

        var limiter = AnomalyReplayTable.GetRateLimiterOrCreate(sender);
        var rateResult = limiter.TryAccept(hash, payloadBytes);
        if (!rateResult.Allowed)
        {
            HandleRateLimitResult(
                sender,
                rateResult,
                $"AnomalyMessaging.OnReceived: Rate-limited hash 0x{hash:X8}",
                localTransport);
            return;
        }

        if (!AnomalyMessageRegistry.TryGetChannel(hash, out var channel))
        {
            RegisterProtocolViolation(
                sender,
                $"AnomalyMessaging.OnReceived: Unknown message hash 0x{hash:X8}",
                localTransport);
            return;
        }

        if (!AnomalyMessageRegistry.TryGetPolicy(hash, out var policy))
        {
            RegisterProtocolViolation(
                sender,
                $"AnomalyMessaging.OnReceived: No policy for hash 0x{hash:X8}",
                localTransport);
            return;
        }
        if (policy.AuthPhase == AnomalyMessageAuthPhase.AuthenticatedSession &&
            !encryptedAndAuthenticated &&
            !localTransport)
        {
            RegisterProtocolViolation(
                sender,
                $"AnomalyMessaging.OnReceived: Dropping pre-auth message hash 0x{hash:X8}",
                localTransport);
            return;
        }
        if (policy.Direction == AnomalyMessageDirection.ServerToClient)
        {
            RegisterProtocolViolation(
                sender,
                $"AnomalyMessaging.OnReceived: Dropping server-to-client message hash 0x{hash:X8} " +
                $"from {sender.LoggedNameFromRefHub()}",
                localTransport);
            return;
        }

        if (payloadBytes > policy.MaxPayloadBytes)
        {
            RegisterProtocolViolation(
                sender,
                $"AnomalyMessaging.OnReceived: Payload for hash 0x{hash:X8} exceeded its ceiling " +
                $"({payloadBytes}/{policy.MaxPayloadBytes} bytes)",
                localTransport);
            return;
        }

        var state = AnomalyReplayTable.GetOrCreate(sender);
        var rx = state.TryAcceptRx(channel, counter, out bool shouldLog);
        if (rx != AnomalyReplayState.RxResult.Accepted)
        {
            if (shouldLog)
            {
                RegisterProtocolViolation(
                    sender,
                    $"AnomalyMessaging.OnReceived: Dropping packet from {sender.LoggedNameFromRefHub()} " +
                    $"(hash=0x{hash:X8}, counter={counter}, channel={channel}, reason={rx})",
                    localTransport);
            }
            else
            {
                var violation = limiter.RegisterProtocolViolation();
                if (violation.ShouldDisconnect)
                    HandleRateLimitResult(sender, violation, "AnomalyMessaging.OnReceived: Sustained replay traffic", localTransport);
            }
            return;
        }

        if (!AnomalyMessageRegistry.TryCreate(hash, out var message))
        {
            // Registered in HashToChannel but not in Factories would be a bug in Register.
            RegisterProtocolViolation(
                sender,
                $"AnomalyMessaging.OnReceived: No factory for hash 0x{hash:X8}",
                localTransport);
            return;
        }

        try
        {
            using (var ms = new MemoryStream(anomalyPayload, 8, anomalyPayload.Length - 8))
            using (var reader = new BinaryReader(ms))
            {
                message.Deserialize(reader);
                if (ms.Position != ms.Length)
                    throw new InvalidDataException(
                        $"Message left {ms.Length - ms.Position} unread payload bytes.");
            }
        }
        catch (Exception ex)
        {
            RegisterProtocolViolation(
                sender,
                $"AnomalyMessaging.OnReceived: Failed to deserialize '{message.MessageName}': " +
                $"{ex.GetType().Name}: {ex.Message}",
                localTransport);
            return;
        }

        if (policy.IsRoundScoped)
        {
            var scoped = message as IRoundScopedAnomalyMessage;
            if (scoped == null)
            {
                RegisterProtocolViolation(
                    sender,
                    $"AnomalyMessaging.OnReceived: Round-scoped policy mismatch for hash 0x{hash:X8}",
                    localTransport);
                return;
            }

            var result = AnomalyReplayTable.GetSequenceOrCreate(sender).TryAccept(
                CurrentRoundEpoch,
                scoped.RoundEpoch,
                scoped.Sequence,
                hash);
            if (result != AnomalySequenceState.RxResult.Accepted)
            {
                RegisterProtocolViolation(
                    sender,
                    $"AnomalyMessaging.OnReceived: Dropping round-scoped packet from " +
                    $"{sender.LoggedNameFromRefHub()} (hash=0x{hash:X8}, " +
                    $"epoch={scoped.RoundEpoch}/{CurrentRoundEpoch}, sequence={scoped.Sequence}, reason={result})",
                    localTransport);
                return;
            }
        }

        foreach (Delegate subscriber in MessageReceived?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            if (!localTransport && !Plugin.IsAuthenticatedAdmissionCurrent(sender))
                return;
            try { ((Action<ReferenceHub, IAnomalyMessage>)subscriber)(sender, message); }
            catch (Exception ex)
            {
                SafeLogError($"AnomalyMessaging.OnReceived: Isolated handler exception for '{message.MessageName}': {ex}");
            }
        }
        if (Plugin.IsAuthenticatedAdmissionCurrent(sender))
        {
            try { Anomaly.Server.Api.Events.Handlers.ClientEvents.OnMessageReceived(sender, message); }
            catch (Exception ex) { SafeLogError($"AnomalyMessaging.MessageReceived publication failed: {ex}"); }
        }
    }

    private static void RegisterProtocolViolation(
        ReferenceHub sender,
        string message,
        bool localTransport)
    {
        if (sender == null)
        {
            SafeLogWarning(message + ".");
            return;
        }

        var result = AnomalyReplayTable.GetRateLimiterOrCreate(sender).RegisterProtocolViolation();
        HandleRateLimitResult(sender, result, message, localTransport);
    }

    private static void HandleRateLimitResult(
        ReferenceHub sender,
        AnomalyRateLimitResult result,
        string message,
        bool localTransport)
    {
        if (result.ShouldDisconnect && !localTransport)
        {
            FailClosedDisconnect(
                sender,
                "AnomalyMessaging.OnReceived: Disconnecting after sustained invalid or over-limit traffic.");
        }
        if (result.ShouldLog)
            SafeLogWarning($"{message} (drops={result.DroppedInWindow}).");
    }

    private static void FailClosedDisconnect(ReferenceHub hub, string message)
    {
        Plugin.QuarantineAuthenticatedPlayer(hub, "anomaly-message-transport-failure");
        try { hub?.connectionToClient?.Disconnect(); }
        catch (Exception exception)
        {
            try { Logger.Warn($"AnomalyMessaging disconnect failed: {exception.Message}"); }
            catch { }
        }
        try { Logger.Error(message); }
        catch { }
    }

    private static void SafeLogError(string message)
    {
        try { Logger.Error(message); }
        catch { }
    }

    private static void SafeLogWarning(string message)
    {
        try { Logger.Warn(message); }
        catch { }
    }

    private static string DescribeHub(ReferenceHub hub)
    {
        if (hub == null)
            return "<null>";
        try { return hub.LoggedNameFromRefHub(); }
        catch { return "<unavailable>"; }
    }

    private static byte[] BuildPayload(
        IAnomalyMessage msg,
        uint counter,
        uint hash,
        AnomalyMessagePolicy policy)
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            // [channel byte][counter][hash][serialized data]
            writer.Write(AnomalyChannel.ChannelId);
            writer.Write(counter);
            writer.Write(hash);
            msg.Serialize(writer);
            writer.Flush();
            int serializedPayloadBytes = checked((int)ms.Length - 9);
            if (serializedPayloadBytes > policy.MaxPayloadBytes ||
                ms.Length > AnomalyChannel.MaxEnvelopeBytes)
            {
                Logger.Warn(
                    $"AnomalyMessaging.SendToClient: '{msg.MessageName}' exceeded its payload ceiling " +
                    $"({serializedPayloadBytes}/{policy.MaxPayloadBytes} bytes).");
                return null;
            }
            return ms.ToArray();
        }
    }

    private static bool FitsDeployedTransport(
        MessageChannel channel,
        int encodedBytes,
        out string failure)
    {
        failure = null;
        try
        {
            var transport = LiteNetLib4MirrorTransport.Singleton;
            if (transport == null)
            {
                failure = "AnomalyMessaging.SendToClient: No deployed transport is active.";
                return false;
            }
            bool fits = TransportPacketPolicy.FitsReportedChannel(
                channel,
                encodedBytes,
                transport.GetMaxPacketSize,
                out int maximum);
            // Reserve framing space for Mirror's message id/length and the encrypted wrapper.
            if (fits)
                return true;
            int required = encodedBytes > int.MaxValue - TransportPacketPolicy.FramingReserveBytes
                ? int.MaxValue
                : encodedBytes + TransportPacketPolicy.FramingReserveBytes;
            failure =
                $"AnomalyMessaging.SendToClient: encoded packet exceeded deployed channel {channel} " +
                $"capacity ({required}/{maximum} bytes).";
            return false;
        }
        catch (Exception exception)
        {
            failure =
                $"AnomalyMessaging.SendToClient: transport ceiling query failed closed: {exception.Message}";
            return false;
        }
    }
}
