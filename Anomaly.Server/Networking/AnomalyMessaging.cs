using System;
using System.IO;
using System.Text;
using Anomaly.Shared.Networking;
using Cryptography;
using GameCore;
using Mirror;
using NetworkManagerUtils.Dummies;
using Org.BouncyCastle.Security;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Networking;

public static class AnomalyMessaging
{
    public static event Action<ReferenceHub, IAnomalyMessage> MessageReceived;

    /// <summary>
    /// Fires once per newly-authenticated hub the moment the Anomaly encrypted
    /// channel is ready to carry messages to that client (i.e. the shared
    /// encryption key has been set on its <c>encryptedChannelManager</c>).
    /// Subsystems that need to push per-player state immediately after auth —
    /// before the round-start snapshot goes out — hook this.
    /// </summary>
    public static event Action<ReferenceHub> ClientReady;

    internal static void RaiseClientReady(ReferenceHub hub)
    {
        try { ClientReady?.Invoke(hub); }
        catch (Exception ex) { Logger.Error($"AnomalyMessaging.ClientReady handler threw: {ex}"); }
    }

    private static readonly SecureRandom SecureRandom = new SecureRandom();

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

        var ecm = hub.encryptedChannelManager;
        var state = AnomalyReplayTable.GetOrCreate(hub);
        uint counter = state.NextTxCounter(msg.TransportChannel);
        var data = BuildPayload(msg, counter);

        EncryptedChannelManager.EncryptedMessageOutside packed;
        if (ecm.isLocalPlayer)
        {
            packed = new EncryptedChannelManager.EncryptedMessageOutside(
                EncryptedChannelManager.SecurityLevel.Unsecured, data);
        }
        else
        {
            if (ecm.EncryptionKey == null)
            {
                Logger.Warn($"AnomalyMessaging.SendToClient: No encryption key for {hub.LoggedNameFromRefHub()}");
                return;
            }
            var encrypted = AES.AesGcmEncrypt(data, ecm.EncryptionKey, SecureRandom);
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

    internal static void OnReceived(ReferenceHub sender, byte[] anomalyPayload)
    {
        // Wire: [4:counter][4:hash][N:payload]
        if (anomalyPayload.Length < 8)
        {
            Logger.Warn("AnomalyMessaging.OnReceived: Payload too short for counter + message hash.");
            return;
        }

        uint counter = BitConverter.ToUInt32(anomalyPayload, 0);
        uint hash = BitConverter.ToUInt32(anomalyPayload, 4);

        if (!AnomalyMessageRegistry.TryGetChannel(hash, out var channel))
        {
            Logger.Warn($"AnomalyMessaging.OnReceived: Unknown message hash 0x{hash:X8}.");
            return;
        }

        var state = AnomalyReplayTable.GetOrCreate(sender);
        var rx = state.TryAcceptRx(channel, counter, out bool shouldLog);
        if (rx != AnomalyReplayState.RxResult.Accepted)
        {
            if (shouldLog)
                Logger.Warn($"AnomalyMessaging.OnReceived: Dropping packet from {sender.LoggedNameFromRefHub()} (hash=0x{hash:X8}, counter={counter}, channel={channel}, reason={rx}).");
            return;
        }

        if (!AnomalyMessageRegistry.TryCreate(hash, out var message))
        {
            // Registered in HashToChannel but not in Factories would be a bug in Register.
            Logger.Warn($"AnomalyMessaging.OnReceived: No factory for hash 0x{hash:X8}.");
            return;
        }

        try
        {
            using (var ms = new MemoryStream(anomalyPayload, 8, anomalyPayload.Length - 8))
            using (var reader = new BinaryReader(ms))
            {
                message.Deserialize(reader);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"AnomalyMessaging.OnReceived: Failed to deserialize '{message.MessageName}': {ex}");
            return;
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

    private static byte[] BuildPayload(IAnomalyMessage msg, uint counter)
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            // [channel byte][counter][hash][serialized data]
            writer.Write(AnomalyChannel.ChannelId);
            writer.Write(counter);
            writer.Write(AnomalyMessageRegistry.GetHash(msg.MessageName));
            msg.Serialize(writer);
            writer.Flush();
            return ms.ToArray();
        }
    }
}
