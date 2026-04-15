using System;
using System.IO;
using System.Text;
using Anomaly.Shared.Networking;
using Cryptography;
using GameCore;
using Mirror;
using Org.BouncyCastle.Security;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Networking;

public static class AnomalyMessaging
{
    public static event Action<ReferenceHub, IAnomalyMessage> MessageReceived;

    private static readonly SecureRandom SecureRandom = new SecureRandom();

    public static void SendToClient(ReferenceHub hub, IAnomalyMessage msg)
    {
        var ecm = hub.encryptedChannelManager;
        var data = BuildPayload(msg);

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
            if (hub.connectionToClient == null) continue;
            SendToClient(hub, msg);
        }
    }

    public static void SendToAllExcept(NetworkConnection exclude, IAnomalyMessage msg)
    {
        foreach (var hub in ReferenceHub.AllHubs)
        {
            if (hub.connectionToClient == null) continue;
            if (hub.connectionToClient == exclude) continue;
            SendToClient(hub, msg);
        }
    }

    internal static void OnReceived(ReferenceHub sender, byte[] anomalyPayload)
    {
        if (anomalyPayload.Length < 4)
        {
            Logger.Warn("AnomalyMessaging.OnReceived: Payload too short for message hash.");
            return;
        }

        uint hash = BitConverter.ToUInt32(anomalyPayload, 0);

        if (!AnomalyMessageRegistry.TryCreate(hash, out var message))
        {
            Logger.Warn($"AnomalyMessaging.OnReceived: Unknown message hash 0x{hash:X8}.");
            return;
        }

        try
        {
            using (var ms = new MemoryStream(anomalyPayload, 4, anomalyPayload.Length - 4))
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

    private static byte[] BuildPayload(IAnomalyMessage msg)
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            // Anomaly channel byte
            writer.Write(AnomalyChannel.ChannelId);
            // Message hash + serialized data
            writer.Write(AnomalyMessageRegistry.GetHash(msg.MessageName));
            msg.Serialize(writer);
            writer.Flush();
            return ms.ToArray();
        }
    }
}
