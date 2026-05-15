using System;
using System.Text;
using Anomaly.Server.Auth;
using Anomaly.Server.Networking;
using Anomaly.Server.Utils;
using Anomaly.Shared.Networking;
using CentralAuth;
using Cryptography;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using MEC;
using Mirror;
using Mirror.LiteNetLib4Mirror;
using NetworkManagerUtils.Dummies;

namespace Anomaly.Server.Patches;

[HarmonyPatch]
public static class PlayerAuthenticationManagerPatch
{
    private const int EncryptedMessageHeaderSize = 5;
    private const int AesGcmNonceSize = 32;
    private const string DummyUserId = "ID_Dummy";
    
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
    

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerAuthenticationManager), nameof(PlayerAuthenticationManager.FixedUpdate))]
    public static void FixedUpdate(PlayerAuthenticationManager __instance)
    {
        if (__instance.isLocalPlayer) return;
        if (__instance._authenticationRequested) return;
        if (!__instance.connectionToClient.isReady) return;
        if (__instance.connectionToClient is DummyNetworkConnection) return;
        if (__instance._hub != null && __instance._hub.IsDummy) return;

        var connectionId = __instance.connectionToClient.connectionId;
        OffThreadLogger.Info("Processing auth in FixedUpdate for connection " + connectionId);

        if (!LiteNetLib4MirrorServer.Peers.TryGetValue(connectionId, out var peer) || peer == null)
        {
            OffThreadLogger.Warn($"Authentication failed for connection {connectionId}: no LiteNetLib peer was found.");
            __instance.RejectAuthentication("Authentication failed Server Side", null, false);
            return;
        }

        var endPoint = peer.EndPoint;
        if(!AuthHandler._storedPlayers.TryGetValue(endPoint,out var player))
        {
            OffThreadLogger.Error($"Player from {endPoint} joined the server without being authenticated. No stored player data found.");
            __instance.RejectAuthentication("Authentication failed Server Side", null, false);
            return;
        }

        __instance._authenticationRequested = true;
        AuthHandler._storedPlayers.Remove(endPoint);

        __instance.UserId = player.UserId;
        __instance._hub.nicknameSync.UpdateNickname(player.NickName);
        __instance._hub.encryptedChannelManager.EncryptionKey = player.SharedKey;
        OffThreadLogger.Info($"Authenticated {player.NickName} ({player.UserId}) from {endPoint}, encryption key set ({player.SharedKey?.Length ?? 0} bytes)");
        __instance._hub.serverRoles.RefreshPermissions(false);

        // Vanilla fires PlayerEvents.OnJoined from
        // PlayerAuthenticationManager.ServerReceiveAuthenticationResponse (the
        // central-auth callback we bypass) and from NicknameSync.CmdSetNick (the
        // modded client doesn't send CmdSetNick — we set the nick server-side
        // above). Without this explicit invocation, no LabAPI plugin sees our
        // hub as "joined", which breaks any plugin hooked to PlayerEvents.Joined.
        try { PlayerEvents.OnJoined(new PlayerJoinedEventArgs(__instance._hub)); }
        catch (Exception ex) { OffThreadLogger.Error($"PlayerEvents.OnJoined dispatch failed: {ex}"); }

        AnomalyMessaging.RaiseClientReady(__instance._hub);
        Timing.CallDelayed(1f, () =>
        {
            __instance._hub.serverRoles.RefreshPermissions(false);
        });
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(DummyUtils), nameof(DummyUtils.SpawnDummy))]
    public static void SpawnDummy(string nickname, ReferenceHub __result)
    {
        if (__result == null)
        {
            OffThreadLogger.Warn("Dummy spawn returned null ReferenceHub.");
            return;
        }

        var safeNickname = nickname ?? "Dummy";

        try
        {
            __result.authManager.UserId = DummyUserId;
            __result.nicknameSync.MyNick = safeNickname;
        }
        catch (Exception ex)
        {
            OffThreadLogger.Error(
                $"Dummy spawn pre-normalization failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }

        Timing.CallDelayed(0.1f, () => NormalizeSpawnedDummy(safeNickname, __result));
    }

    private static void NormalizeSpawnedDummy(string nickname, ReferenceHub hub)
    {
        if (hub == null)
        {
            return;
        }

        try
        {
            hub.authManager.UserId = DummyUserId;
            hub.nicknameSync.MyNick = nickname;

            if (hub.serverRoles.Group != DummyUtils.DummyGroup)
            {
                hub.serverRoles.SetGroup(DummyUtils.DummyGroup, byAdmin: true);
            }

            PlayerEvents.OnJoined(new PlayerJoinedEventArgs(hub));
            OffThreadLogger.Info($"Dummy player joined: userId={hub.authManager.UserId} nick={hub.nicknameSync.MyNick} playerId={hub.PlayerId} mode={hub.authManager.InstanceMode}");
        }
        catch (Exception ex)
        {
            OffThreadLogger.Error(
                $"Dummy spawn normalization failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EncryptedChannelManager), nameof(EncryptedChannelManager.TrySendMessageToClient), new Type[] { typeof(byte[]), typeof(EncryptedChannelManager.EncryptedChannel) })]
    public static bool OnPackBytes(EncryptedChannelManager __instance, out bool __result,
        byte[] content, EncryptedChannelManager.EncryptedChannel channel)
    {
        // Vanilla DummyNetworkConnection.Send is a no-op; report success
        // without burning a counter slot or building the payload. Returning
        // false here (the prior behavior) caused vanilla callers like
        // serverRoles.SetGroup to throw during dummy spawn, which the
        // surrounding try/catch swallowed and prevented PlayerEvents.OnJoined.
        if (__instance.connectionToClient is DummyNetworkConnection)
        {
            __result = true;
            return false;
        }

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
        if (__instance.connectionToClient is DummyNetworkConnection)
        {
            __result = true;
            return false;
        }

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
        if (!NetworkServer.active)
            return false;

        if (!ReferenceHub.TryGetHub(conn, out var hub))
        {
            OffThreadLogger.Warn(
                $"Encrypted message dropped: no hub for connection {conn?.connectionId ?? -1} " +
                $"(isReady={conn?.isReady}, identity={(conn?.identity == null ? "null" : conn.identity.netId.ToString())}).");
            return false;
        }

        var packedData = packed.Data;
        if (packedData == null)
        {
            OffThreadLogger.Warn(
                $"Encrypted message dropped from {hub.LoggedNameFromRefHub()}: payload was null " +
                $"(connId={conn?.connectionId ?? -1}, level={packed.Level}).");
            return false;
        }

        if (packedData.Length == 0)
        {
            OffThreadLogger.Warn(
                $"Encrypted message dropped from {hub.LoggedNameFromRefHub()}: payload was empty " +
                $"(connId={conn?.connectionId ?? -1}, level={packed.Level}).");
            return false;
        }

        EncryptedChannelManager.EncryptedChannel channel;
        uint counter;
        byte[] content;
        EncryptedChannelManager.EncryptedMessage message;

        var localClient = conn?.identity != null && conn.identity.isLocalPlayer;
        if (localClient)
        {
            channel = (EncryptedChannelManager.EncryptedChannel)packedData[0];

            // Anomaly custom messages -- separate dispatch. Wire is
            // [0xFE][counter][hash][payload]; AnomalyMessaging.OnReceived
            // parses the counter/hash and applies replay protection.
            if ((byte)channel == AnomalyChannel.ChannelId)
            {
                var anomalyPayload = new byte[packedData.Length - 1];
                Array.Copy(packedData, 1, anomalyPayload, 0, anomalyPayload.Length);
                AnomalyMessaging.OnReceived(hub, anomalyPayload);
                return false;
            }

            if (packedData.Length < EncryptedMessageHeaderSize)
            {
                OffThreadLogger.Warn(
                    $"Encrypted message dropped from local client {hub.LoggedNameFromRefHub()}: " +
                    $"payload too short ({packedData.Length} bytes).");
                return false;
            }

            counter = BitConverter.ToUInt32(packedData, 1);
            content = new byte[packedData.Length - EncryptedMessageHeaderSize];
            Array.Copy(packedData, EncryptedMessageHeaderSize, content, 0, content.Length);
            message = new EncryptedChannelManager.EncryptedMessage(channel, content, counter);
        }
        else
        {
            var encrypted = packed.Level == EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated;
            if (!encrypted && packed.Level != EncryptedChannelManager.SecurityLevel.Unsecured)
            {
                OffThreadLogger.Warn(
                    $"Encrypted message dropped from {hub.LoggedNameFromRefHub()}: unknown security level {packed.Level}.");
                return false;
            }

            byte[] decryptedData;
            if (encrypted)
            {
                var encryptionKey = hub.encryptedChannelManager.EncryptionKey;
                if (encryptionKey == null)
                {
                    OffThreadLogger.Warn("Got EncryptedMessage eventhough no Key was set");
                    return false;
                }

                if (packedData.Length < AesGcmNonceSize)
                {
                    OffThreadLogger.Warn(
                        $"Encrypted message dropped from {hub.LoggedNameFromRefHub()}: encrypted payload too short " +
                        $"({packedData.Length} bytes).");
                    return false;
                }

                try
                {
                    decryptedData = AES.AesGcmDecrypt(packedData, encryptionKey);
                }
                catch (Exception ex)
                {
                    OffThreadLogger.Warn(
                        $"Failed to decrypt received message from {hub.LoggedNameFromRefHub()} " +
                        $"(connId={conn?.connectionId ?? -1}, bytes={packedData.Length}): {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
            }
            else
            {
                decryptedData = packedData;
            }

            if (decryptedData == null || decryptedData.Length == 0)
            {
                OffThreadLogger.Warn(
                    $"Encrypted message dropped from {hub.LoggedNameFromRefHub()}: " +
                    $"decoded payload was {(decryptedData == null ? "null" : "empty")}.");
                return false;
            }

            channel = (EncryptedChannelManager.EncryptedChannel)decryptedData[0];

            if (!encrypted && RequiresEncryption(channel))
            {
                OffThreadLogger.Warn($"Message on channel {channel} was sent without encryption. Discarding message!");
                return false;
            }

            // Anomaly custom messages -- separate dispatch. Wire is
            // [0xFE][counter][hash][payload]; AnomalyMessaging.OnReceived
            // parses the counter/hash and applies replay protection.
            if ((byte)channel == AnomalyChannel.ChannelId)
            {
                var anomalyPayload = new byte[decryptedData.Length - 1];
                Array.Copy(decryptedData, 1, anomalyPayload, 0, anomalyPayload.Length);
                AnomalyMessaging.OnReceived(hub, anomalyPayload);
                return false;
            }

            if (decryptedData.Length < EncryptedMessageHeaderSize)
            {
                OffThreadLogger.Warn(
                    $"Encrypted message dropped from {hub.LoggedNameFromRefHub()}: decoded payload too short " +
                    $"({decryptedData.Length} bytes, channel={channel}, level={packed.Level}).");
                return false;
            }

            counter = BitConverter.ToUInt32(decryptedData, 1);
            content = new byte[decryptedData.Length - EncryptedMessageHeaderSize];
            Array.Copy(decryptedData, EncryptedMessageHeaderSize, content, 0, content.Length);
            message = new EncryptedChannelManager.EncryptedMessage(channel, content, counter);

            if (encrypted)
            {
                if (hub.encryptedChannelManager._rxCounter == 4294967295U)
                    hub.encryptedChannelManager._rxCounter = 0;

                if (counter <= hub.encryptedChannelManager._rxCounter)
                {
                    OffThreadLogger.Warn($"Received message with counter {counter}, which is lower or equal to last received message counter {hub.encryptedChannelManager._rxCounter}. Discarding message!");
                    return false;
                }

                hub.encryptedChannelManager._rxCounter = counter;
            }
        }


        if (!EncryptedChannelManager.ServerChannelHandlers.TryGetValue(channel, out var handler))
        {
            OffThreadLogger.Warn($"No handler is registered for encrypted channel {channel} (server).");
            return false;
        }

        try
        {
            handler.Invoke(hub, message, packed.Level);
        }
        catch (Exception ex)
        {
            OffThreadLogger.Error($"Exception while handling encrypted message on channel {channel} (server, running a handler). Exception: {ex.Message}");
            OffThreadLogger.Error(ex.StackTrace);
        }
        return false;
    }

    private static bool RequiresEncryption(EncryptedChannelManager.EncryptedChannel channel)
    {
        return channel == EncryptedChannelManager.EncryptedChannel.RemoteAdmin ||
               channel == EncryptedChannelManager.EncryptedChannel.AdminChat;
    }
}
