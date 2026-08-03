using System;
using System.IO;
using System.Text;
using Anomaly.Shared.Networking;
using GameCore;
using Mirror;
using NetworkManagerUtils.Dummies;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Networking;

public static class AnomalyMessaging
{
    public static event Action<ReferenceHub, IAnomalyMessage> MessageReceived;

    /// <summary>
    /// Fires once per newly-authenticated hub the moment the Anomaly encrypted
    /// channel is ready to carry messages to that client (i.e. its directional
    /// protocol-v2 transport session has been registered).
    /// Subsystems that need to push per-player state immediately after auth —
    /// before the round-start snapshot goes out — hook this.
    /// </summary>
    public static event Action<ReferenceHub> ClientReady;

    internal static void RaiseClientReady(ReferenceHub hub)
    {
        try { ClientReady?.Invoke(hub); }
        catch (Exception ex) { Logger.Error($"AnomalyMessaging.ClientReady handler threw: {ex}"); }
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
        if (conn == null) return false;
        if (conn is DummyNetworkConnection) return false;
        return true;
    }

    public static void SendToClient(ReferenceHub hub, IAnomalyMessage msg)
    {
        if (!IsAnomalyTarget(hub)) return;

        uint hash = AnomalyMessageRegistry.GetHash(msg.MessageName);
        if (!AnomalyMessageRegistry.TryGetPolicy(hash, out var policy))
        {
            Logger.Warn($"AnomalyMessaging.SendToClient: No policy registered for '{msg.MessageName}'.");
            return;
        }
        if (policy.Direction == AnomalyMessageDirection.ClientToServer)
        {
            Logger.Warn($"AnomalyMessaging.SendToClient: '{msg.MessageName}' is client-to-server only.");
            return;
        }

        var state = AnomalyReplayTable.GetOrCreate(hub);
        uint counter = state.NextTxCounter(msg.TransportChannel);
        var data = BuildPayload(msg, counter, hash, policy);
        if (data == null) return;

        EncryptedChannelManager.EncryptedMessageOutside packed;
        if (hub.encryptedChannelManager.isLocalPlayer)
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
                    Logger.Warn($"AnomalyMessaging.SendToClient: No protocol-v2 transport session for {hub.LoggedNameFromRefHub()}");
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"AnomalyMessaging.SendToClient: Encryption failed for {hub.LoggedNameFromRefHub()}: {ex.GetType().Name}: {ex.Message}");
                return;
            }
            packed = new EncryptedChannelManager.EncryptedMessageOutside(
                EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated, encrypted);
        }

        hub.connectionToClient.Send(packed, (int)msg.TransportChannel);
    }

    public static void SendToAll(IAnomalyMessage msg)
    {
        foreach (var hub in ReferenceHub.AllHubs)
        {
            if (!IsAnomalyTarget(hub)) continue;
            SendToClient(hub, msg);
        }
    }

    public static void SendToAllExcept(NetworkConnection exclude, IAnomalyMessage msg)
    {
        foreach (var hub in ReferenceHub.AllHubs)
        {
            if (!IsAnomalyTarget(hub)) continue;
            if (hub.connectionToClient == exclude) continue;
            SendToClient(hub, msg);
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

        try
        {
            MessageReceived?.Invoke(sender, message);
        }
        catch (Exception ex)
        {
            Logger.Error($"AnomalyMessaging.OnReceived: Handler exception for '{message.MessageName}': {ex}");
        }
    }

    private static void RegisterProtocolViolation(
        ReferenceHub sender,
        string message,
        bool localTransport)
    {
        if (sender == null)
        {
            Logger.Warn(message + ".");
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
        if (result.ShouldLog)
            Logger.Warn($"{message} (drops={result.DroppedInWindow}).");
        if (!result.ShouldDisconnect || localTransport || sender?.connectionToClient == null)
            return;

        Logger.Error(
            $"AnomalyMessaging.OnReceived: Disconnecting {sender.LoggedNameFromRefHub()} after " +
            "sustained invalid or over-limit traffic.");
        try
        {
            sender.connectionToClient.Disconnect();
        }
        catch (Exception ex)
        {
            Logger.Warn($"AnomalyMessaging.OnReceived: Rate-limit disconnect failed: {ex.Message}");
        }
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
}
