using System;
using System.Text;
using Anomaly.Server.Auth;
using Anomaly.Server.Networking;
using Anomaly.Server.Utils;
using Anomaly.Shared.Networking;
using CentralAuth;
using Cryptography;
using HarmonyLib;
using MEC;
using Mirror;
using Mirror.LiteNetLib4Mirror;

namespace Anomaly.Server.Patches;

[HarmonyPatch]
public static class PlayerAuthenticationManagerPatch
{
    
    /*[HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerAuthenticationManager), nameof(PlayerAuthenticationManager.Start))]
    public static void Start(PlayerAuthenticationManager __instance)
    {
        if (__instance.isLocalPlayer) return;
        var endPoint = LiteNetLib4MirrorServer.Peers[__instance.connectionToClient.connectionId].EndPoint;
        var player = ProcessConnectionRequestPatch.VerifiedPlayers[endPoint];
        __instance.UserId = player.UserId;
        __instance._hub.nicknameSync.UpdateNickname(player.Name);
    }*/
    

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAuthenticationManager), nameof(PlayerAuthenticationManager.FixedUpdate))]
    public static bool FixedUpdate(PlayerAuthenticationManager __instance)
    {
        if (__instance.isLocalPlayer) return true; // Let vanilla handle the host player
        if (__instance._authenticationRequested) return false; // Already authed by us, skip vanilla
        if (!__instance.connectionToClient.isReady) return false; // Not ready yet, but don't let vanilla run either

        OffThreadLogger.Info("Processing auth in FixedUpdate for connection " + __instance.connectionToClient.connectionId);

        var endPoint = LiteNetLib4MirrorServer.Peers[__instance.connectionToClient.connectionId].EndPoint;
        if(!AuthHandler._storedPlayers.TryGetValue(endPoint,out var player))
        {
            OffThreadLogger.Error($"Player from {endPoint} joined the server without being authenticated. No stored player data found.");
            __instance.RejectAuthentication("Authentication failed Server Side", null, false);
            return false;
        }

        __instance._authenticationRequested = true;
        AuthHandler._storedPlayers.Remove(endPoint);

        __instance.UserId = player.UserId;
        __instance._hub.nicknameSync.UpdateNickname(player.NickName);
        __instance._hub.encryptedChannelManager.EncryptionKey = player.SharedKey;
        OffThreadLogger.Info($"Authenticated {player.NickName} ({player.UserId}) from {endPoint}, encryption key set ({player.SharedKey?.Length ?? 0} bytes)");
        Timing.CallDelayed(1f, () =>
        {
            __instance._hub.serverRoles.RefreshPermissions(false);
            //Exiled.Events.Patches.Events.Player.Verified.Postfix(__instance);
        });
        return false; // Skip vanilla FixedUpdate entirely
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EncryptedChannelManager), nameof(EncryptedChannelManager.TrySendMessageToClient), new Type[] { typeof(byte[]), typeof(EncryptedChannelManager.EncryptedChannel) })]
    public static bool OnPackBytes(EncryptedChannelManager __instance, out bool __result,
        byte[] content, EncryptedChannelManager.EncryptedChannel channel)
    {
        if (__instance._txCounter == 4294967295U)
            __instance._txCounter = 0;
        __instance._txCounter++;

        EncryptedChannelManager.EncryptedMessageOutside messageOut;

        var data = new byte[content.Length + 5];
        data[0] = (byte)channel;
        BitConverter.GetBytes(__instance._txCounter).CopyTo(data, 1);
        Array.Copy(content, 0, data, 5, content.Length);

        if (__instance.isLocalPlayer)
        {
            messageOut = new EncryptedChannelManager.EncryptedMessageOutside(EncryptedChannelManager.SecurityLevel.Unsecured, data);
        }
        else
        {
            if (__instance.EncryptionKey == null)
            {
                OffThreadLogger.Warn("Tried to send encrypted byte[] message, but no key was found");
                __result = false;
                return false;
            }
            var encryptedData = Cryptography.AES.AesGcmEncrypt(data, __instance.EncryptionKey, EncryptedChannelManager.SecureRandom);
            messageOut = new EncryptedChannelManager.EncryptedMessageOutside(EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated, encryptedData);
        }

        __instance.connectionToClient.Send(messageOut);
        __result = true;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EncryptedChannelManager), nameof(EncryptedChannelManager.TrySendMessageToClient), new Type[] { typeof(string), typeof(EncryptedChannelManager.EncryptedChannel) })]
    public static bool OnPack(EncryptedChannelManager __instance, out bool __result,
        string content, EncryptedChannelManager.EncryptedChannel channel)
    {
        if (__instance._txCounter == 4294967295U)
            __instance._txCounter = 0;
        __instance._txCounter++;

        EncryptedChannelManager.EncryptedMessageOutside messageOut;

        var data = new byte[Misc.Utf8Encoding.GetByteCount(content)+5];
        data[0] = (byte)channel;
        BitConverter.GetBytes(__instance._txCounter).CopyTo(data, 1);
        Encoding.UTF8.GetBytes(content, 0, content.Length, data, 5);

        if (__instance.isLocalPlayer)
        {
            messageOut = new EncryptedChannelManager.EncryptedMessageOutside(EncryptedChannelManager.SecurityLevel.Unsecured, data);
        }
        else
        {
            if (__instance.EncryptionKey == null)
            {
                OffThreadLogger.Warn("Tried to send encrypted message, but no key was found");
                __result = false;
                return false;
            }
            var encryptedData = Cryptography.AES.AesGcmEncrypt(data, __instance.EncryptionKey, EncryptedChannelManager.SecureRandom);
            messageOut = new EncryptedChannelManager.EncryptedMessageOutside(EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated, encryptedData);
        }

        __instance.connectionToClient.Send(messageOut);
        __result = true;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EncryptedChannelManager), nameof(EncryptedChannelManager.ServerReceivePackedMessage))]
    public static bool OnReceivePackedMessage(NetworkConnection conn, EncryptedChannelManager.EncryptedMessageOutside packed)
    {
        if (!ReferenceHub.TryGetHub(conn.identity.gameObject, out var hub))
            return false;

        EncryptedChannelManager.EncryptedChannel channel;
        uint counter;
        string content;
        EncryptedChannelManager.EncryptedMessage message;

        if (conn.identity.isLocalPlayer)
        {
            channel = (EncryptedChannelManager.EncryptedChannel)packed.Data[0];

            // Anomaly custom messages -- separate dispatch, no counter
            if ((byte)channel == AnomalyChannel.ChannelId)
            {
                var anomalyPayload = new byte[packed.Data.Length - 1];
                Array.Copy(packed.Data, 1, anomalyPayload, 0, anomalyPayload.Length);
                AnomalyMessaging.OnReceived(hub, anomalyPayload);
                return false;
            }

            counter = BitConverter.ToUInt32(packed.Data, 1);
            content = Encoding.UTF8.GetString(packed.Data, 5, packed.Data.Length - 5);
            message = new EncryptedChannelManager.EncryptedMessage(channel, content, counter);
        }
        else
        {
            if (hub.encryptedChannelManager.EncryptionKey == null)
            {
                OffThreadLogger.Warn("Got EncryptedMessage eventhough no Key was set");
                return false;
            }

            var decryptedData = AES.AesGcmDecrypt(packed.Data, hub.encryptedChannelManager.EncryptionKey);

            channel = (EncryptedChannelManager.EncryptedChannel)decryptedData[0];

            // Anomaly custom messages -- separate dispatch, no counter
            if ((byte)channel == AnomalyChannel.ChannelId)
            {
                var anomalyPayload = new byte[decryptedData.Length - 1];
                Array.Copy(decryptedData, 1, anomalyPayload, 0, anomalyPayload.Length);
                AnomalyMessaging.OnReceived(hub, anomalyPayload);
                return false;
            }

            counter = BitConverter.ToUInt32(decryptedData, 1);
            content = Encoding.UTF8.GetString(decryptedData, 5, decryptedData.Length - 5);
            message = new EncryptedChannelManager.EncryptedMessage(channel, content, counter);


            if (hub.encryptedChannelManager._rxCounter == 4294967295U)
                hub.encryptedChannelManager._rxCounter = 0;

            if (counter <= hub.encryptedChannelManager._rxCounter)
            {
                OffThreadLogger.Warn($"Received message with counter {counter}, which is lower or equal to last received message counter {hub.encryptedChannelManager._rxCounter}. Discarding message!");
                return false;
            }

            hub.encryptedChannelManager._rxCounter = counter;
        }


        if (!EncryptedChannelManager.ServerChannelHandlers.ContainsKey(channel))
        {
            OffThreadLogger.Warn($"No handler is registered for encrypted channel {channel} (server).");
            return false;
        }

        try
        {
            EncryptedChannelManager.ServerChannelHandlers[channel].Invoke(hub, message, packed.Level);
        }
        catch (Exception ex)
        {
            OffThreadLogger.Error($"Exception while handling encrypted message on channel {channel} (server, running a handler). Exception: {ex.Message}");
            OffThreadLogger.Error(ex.StackTrace);
        }
        return false;
    }
}
