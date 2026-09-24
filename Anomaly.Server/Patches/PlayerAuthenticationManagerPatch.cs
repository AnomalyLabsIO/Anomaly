using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Anomaly.Server.Auth;
using Anomaly.Server.Networking;
using Anomaly.Server.Utils;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using CentralAuth;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using MEC;
using Mirror;
using Mirror.LiteNetLib4Mirror;
using NetworkManagerUtils.Dummies;

namespace Anomaly.Server.Patches;

[HarmonyPatch]
internal static class PlayerAuthenticationManagerPatch
{
    private const int EncryptedMessageHeaderSize = 5;
    private const uint GenericTransportRateKeyPrefix = 0xFFF00000u;
    private const uint RawTransportRateKey = 0xFFE00000u;
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
        try { OffThreadLogger.Info("Processing auth in FixedUpdate for connection " + connectionId); }
        catch { }

        if (!LiteNetLib4MirrorServer.Peers.TryGetValue(connectionId, out var peer) || peer == null)
        {
            RejectFailClosed(__instance, "missing-litenetlib-peer", "Authentication failed Server Side");
            try { OffThreadLogger.Warn($"Authentication failed for connection {connectionId}: no LiteNetLib peer was found."); }
            catch { }
            return;
        }

        var endPoint = peer.EndPoint;
        AuthenticatedHandoff handoff = null;
        string handoffFailure = null;
        bool admissionEntered = Plugin.TryCommitAuthenticatedAdmission(() =>
        {
            PlayerStorage player = null;
            try
            {
                // Adopt the same connection-owned keys and counters used for map bootstrap.
                if (!AuthenticatedTransportSessions.TryAdoptConnection(__instance._hub, out player) ||
                    !ReferenceEquals(player.AcceptedPeer, peer))
                    throw new InvalidOperationException("No matching authenticated connection session was found.");

                handoff = PrepareAuthenticatedHandoff(__instance, endPoint, player);
            }
            catch (Exception ex)
            {
                if (player != null)
                {
                    try { EnrollmentSessionRegistry.Cancel(player.UserId, player.EnrollmentReservation); }
                    catch { }
                    try { player.ClearSecrets(); }
                    catch { }
                }
                Plugin.QuarantineAuthenticatedPlayer(__instance._hub, "authentication-handoff-failure");
                handoffFailure = $"Authentication handoff failed for {player?.UserId ?? "unknown"}: {ex}";
            }
        });

        if (!admissionEntered)
        {
            // The still-stored handoff is drained by teardown, which latches the
            // deny-all patch barrier. Revoke this hub as well before diagnostics so
            // a failed rejection cannot leave an offline ReadyClient behind.
            RejectFailClosed(
                __instance,
                "admission-closed-during-handoff",
                "Anomaly authentication is unavailable during server teardown.");
            try { OffThreadLogger.Warn("Authentication was rejected because Anomaly admission closed during handoff."); }
            catch { }
            return;
        }

        if (handoff == null)
        {
            RejectFailClosed(__instance, "authentication-handoff-failure", "Authentication failed Server Side");
            try { OffThreadLogger.Error(handoffFailure ?? "Authentication handoff failed without a diagnostic."); }
            catch { }
            return;
        }

        PublishAuthenticatedHandoff(handoff);
    }

    private static void RejectFailClosed(
        PlayerAuthenticationManager instance,
        string context,
        string rejectionReason)
    {
        Plugin.QuarantineAuthenticatedPlayer(instance?._hub, context);
        try { instance?.connectionToClient?.Disconnect(); }
        catch { }
        try { instance?.RejectAuthentication(rejectionReason, null, false); }
        catch { }
    }

    private static AuthenticatedHandoff PrepareAuthenticatedHandoff(
        PlayerAuthenticationManager instance,
        System.Net.IPEndPoint endPoint,
        PlayerStorage player)
    {
        instance._authenticationRequested = true;

        instance.UserId = player.UserId;
        instance._hub.nicknameSync.UpdateNickname(player.NickName);
        instance._hub.encryptedChannelManager.EncryptionKey = null;

        if (!EnrollmentSessionRegistry.TryActivate(
                player.UserId,
                player.EnrollmentReservation,
                instance._hub,
                PlayerAuthenticationManager.AllowSameAccountJoining,
                out var previousEnrollmentHub))
        {
            EnrollmentSessionRegistry.Cancel(player.UserId, player.EnrollmentReservation);
            throw new InvalidOperationException("The authentication was superseded by a newer connection attempt.");
        }

        var duplicates = new List<ReferenceHub>();
        if (!PlayerAuthenticationManager.AllowSameAccountJoining)
        {
            duplicates = ReferenceHub.AllHubs
                .Where(hub => hub != null
                              && !ReferenceEquals(hub, instance._hub)
                              && !hub.isLocalPlayer
                              && string.Equals(hub.authManager?.UserId, player.UserId, StringComparison.Ordinal))
                .ToList();
            if (previousEnrollmentHub != null
                && !duplicates.Any(hub => ReferenceEquals(hub, previousEnrollmentHub)))
            {
                duplicates.Add(previousEnrollmentHub);
            }
        }

        return new AuthenticatedHandoff
        {
            Manager = instance,
            EndPoint = endPoint,
            UserId = player.UserId,
            NickName = player.NickName,
            DuplicateHubs = duplicates,
        };
    }

    private static void PublishAuthenticatedHandoff(AuthenticatedHandoff handoff)
    {
        ReferenceHub survivingOlderHub = null;
        foreach (ReferenceHub duplicate in handoff.DuplicateHubs)
        {
            if (!Plugin.IsAuthenticatedAdmissionCurrent(handoff.Manager._hub))
                return;
            try
            {
                ServerConsole.Disconnect(
                    duplicate.gameObject,
                    "Only one player instance of the same player is allowed.");
            }
            catch (Exception ex)
            {
                survivingOlderHub ??= duplicate;
                try { OffThreadLogger.Error($"Failed to disconnect an older session for {handoff.UserId}: {ex}"); }
                catch { }
            }
        }

        if (survivingOlderHub != null)
        {
            bool rolledBack = false;
            Plugin.TryCommitAuthenticatedAdmission(() =>
            {
                rolledBack = EnrollmentSessionRegistry.TryRollbackActivation(
                    handoff.UserId,
                    handoff.Manager._hub,
                    survivingOlderHub);
                Plugin.QuarantineAuthenticatedPlayer(handoff.Manager._hub, "duplicate-session-rollback");
            });
            RejectFailClosed(
                handoff.Manager,
                "duplicate-session-rollback",
                "The older authenticated session could not be replaced safely.");
            try
            {
                OffThreadLogger.Error(
                    $"Rejected replacement session for {handoff.UserId} because an older authenticated " +
                    $"session could not be evicted (conditionalRollback={rolledBack}).");
            }
            catch { }
            return;
        }

        if (!Plugin.IsAuthenticatedAdmissionCurrent(handoff.Manager._hub))
            return;

        try { handoff.Manager._hub.serverRoles.RefreshPermissions(false); }
        catch (Exception exception)
        {
            RejectFailClosed(handoff.Manager, "permission-refresh-failure", "Authentication failed Server Side");
            try { OffThreadLogger.Error($"Initial permission refresh failed for {handoff.UserId}: {exception}"); }
            catch { }
            return;
        }

        // Vanilla fires PlayerEvents.OnJoined from
        // PlayerAuthenticationManager.ServerReceiveAuthenticationResponse (the
        // central-auth callback we bypass) and from NicknameSync.CmdSetNick (the
        // modded client doesn't send CmdSetNick — we set the nick server-side
        // above). Without this explicit invocation, no LabAPI plugin sees our
        // hub as "joined", which breaks any plugin hooked to PlayerEvents.Joined.
        try { PlayerEvents.OnJoined(new PlayerJoinedEventArgs(handoff.Manager._hub)); }
        catch (Exception ex)
        {
            try { OffThreadLogger.Error($"PlayerEvents.OnJoined dispatch failed: {ex}"); }
            catch { }
        }

        if (!Plugin.IsAuthenticatedAdmissionCurrent(handoff.Manager._hub))
            return;
        AnomalyMessaging.RaiseClientReady(handoff.Manager._hub);
        if (!Plugin.IsAuthenticatedAdmissionCurrent(handoff.Manager._hub))
            return;
        try { Timing.CallDelayed(1f, () =>
        {
            if (!Plugin.IsAuthenticatedAdmissionCurrent(handoff.Manager._hub))
                return;
            try { handoff.Manager._hub.serverRoles.RefreshPermissions(false); }
            catch (Exception exception)
            {
                RejectFailClosed(handoff.Manager, "delayed-permission-refresh-failure", "Authentication failed Server Side");
                try { OffThreadLogger.Error($"Delayed permission refresh failed for {handoff.UserId}: {exception}"); }
                catch { }
            }
        }); }
        catch (Exception exception)
        {
            try { OffThreadLogger.Error($"Could not schedule delayed permission refresh: {exception}"); }
            catch { }
        }
        try
        {
            OffThreadLogger.Info(
                $"Authenticated {handoff.NickName} ({handoff.UserId}) from {handoff.EndPoint}, " +
                "directional authenticated transport enabled");
        }
        catch { }
    }

    private sealed class AuthenticatedHandoff
    {
        internal PlayerAuthenticationManager Manager;
        internal System.Net.IPEndPoint EndPoint;
        internal string UserId;
        internal string NickName;
        internal List<ReferenceHub> DuplicateHubs;
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

        if (!EncryptedTransportCounterGuard.TryAdvance(__instance._txCounter, out var nextCounter))
        {
            ExhaustTransportCounter(__instance);
            __result = false;
            return false;
        }
        __instance._txCounter = nextCounter;

        EncryptedChannelManager.EncryptedMessageOutside messageOut;
        ReferenceHub targetHub = null;

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
            if (!ReferenceHub.TryGetHub(__instance.connectionToClient, out targetHub))
            {
                FailClosedOutgoingTransport(
                    __instance,
                    null,
                    "encrypted-byte-send-missing-hub",
                    "Tried to send encrypted byte[] message, but no hub was found for the connection.");
                __result = false;
                return false;
            }
            byte[] encryptedData;
            try
            {
                if (!AuthenticatedTransportSessions.TryEncryptToClient(targetHub, data, out encryptedData))
                {
                    FailClosedOutgoingTransport(
                        __instance,
                        targetHub,
                        "encrypted-byte-send-missing-session",
                        "Tried to send encrypted byte[] message, but no directional authenticated transport session was found.");
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                FailClosedOutgoingTransport(
                    __instance,
                    targetHub,
                    "encrypted-byte-send-encryption-failure",
                    $"Failed to encrypt server-to-client byte[] message: {ex.GetType().Name}: {ex.Message}");
                __result = false;
                return false;
            }
            messageOut = new EncryptedChannelManager.EncryptedMessageOutside(EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated, encryptedData);
        }

        try
        {
            __instance.connectionToClient.Send(messageOut);
        }
        catch (Exception ex)
        {
            FailClosedOutgoingTransport(
                __instance,
                targetHub,
                "encrypted-byte-send-delivery-failure",
                $"Failed to deliver server-to-client encrypted byte[] message: {ex.GetType().Name}: {ex.Message}");
            __result = false;
            return false;
        }
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

        if (!EncryptedTransportCounterGuard.TryAdvance(__instance._txCounter, out var nextCounter))
        {
            ExhaustTransportCounter(__instance);
            __result = false;
            return false;
        }
        __instance._txCounter = nextCounter;

        EncryptedChannelManager.EncryptedMessageOutside messageOut;
        ReferenceHub targetHub = null;

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
            if (!ReferenceHub.TryGetHub(__instance.connectionToClient, out targetHub))
            {
                FailClosedOutgoingTransport(
                    __instance,
                    null,
                    "encrypted-string-send-missing-hub",
                    "Tried to send encrypted message, but no hub was found for the connection.");
                __result = false;
                return false;
            }
            byte[] encryptedData;
            try
            {
                if (!AuthenticatedTransportSessions.TryEncryptToClient(targetHub, data, out encryptedData))
                {
                    FailClosedOutgoingTransport(
                        __instance,
                        targetHub,
                        "encrypted-string-send-missing-session",
                        "Tried to send encrypted message, but no directional authenticated transport session was found.");
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                FailClosedOutgoingTransport(
                    __instance,
                    targetHub,
                    "encrypted-string-send-encryption-failure",
                    $"Failed to encrypt server-to-client message: {ex.GetType().Name}: {ex.Message}");
                __result = false;
                return false;
            }
            messageOut = new EncryptedChannelManager.EncryptedMessageOutside(EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated, encryptedData);
        }

        try
        {
            __instance.connectionToClient.Send(messageOut);
        }
        catch (Exception ex)
        {
            FailClosedOutgoingTransport(
                __instance,
                targetHub,
                "encrypted-string-send-delivery-failure",
                $"Failed to deliver server-to-client encrypted message: {ex.GetType().Name}: {ex.Message}");
            __result = false;
            return false;
        }
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
            try { conn?.Disconnect(); }
            catch { }
            try
            {
                OffThreadLogger.Warn(
                    $"Encrypted message dropped: no hub for connection {conn?.connectionId ?? -1} " +
                    $"(isReady={conn?.isReady}, identity={(conn?.identity == null ? "null" : conn.identity.netId.ToString())}).");
            }
            catch { }
            return false;
        }

        var packedData = packed.Data;
        var rawLimiter = AnomalyReplayTable.GetRawRateLimiterOrCreate(hub);
        if (packedData == null)
        {
            HandleRawTransportResult(
                hub,
                conn,
                rawLimiter.RegisterProtocolViolation(),
                $"Encrypted message payload was null (connId={conn?.connectionId ?? -1}, level={packed.Level}).");
            return false;
        }

        if (packedData.Length == 0)
        {
            HandleRawTransportResult(
                hub,
                conn,
                rawLimiter.RegisterProtocolViolation(),
                $"Encrypted message payload was empty (connId={conn?.connectionId ?? -1}, level={packed.Level}).");
            return false;
        }

        if (packedData.Length > AnomalyChannel.MaxEncryptedEnvelopeBytes)
        {
            HandleRawTransportResult(
                hub,
                conn,
                rawLimiter.RegisterProtocolViolation(),
                $"Encrypted message exceeded the absolute ceiling " +
                $"({packedData.Length}/{AnomalyChannel.MaxEncryptedEnvelopeBytes} bytes).");
            return false;
        }

        var rawRateResult = rawLimiter.TryAccept(
            RawTransportRateKey,
            Math.Min(packedData.Length, AnomalyChannel.MaxEnvelopeBytes));
        if (!rawRateResult.Allowed)
        {
            HandleRawTransportResult(
                hub,
                conn,
                rawRateResult,
                $"Pre-decrypt traffic exceeded bounded packet/byte limits ({packedData.Length} bytes).");
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
                AnomalyMessaging.OnReceived(
                    hub,
                    anomalyPayload,
                    packed.Level == EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated,
                    true);
                return false;
            }

            if (packedData.Length < EncryptedMessageHeaderSize)
            {
                HandleRawTransportResult(
                    hub,
                    conn,
                    rawLimiter.RegisterProtocolViolation(),
                    $"Local encrypted-channel payload was too short ({packedData.Length} bytes).");
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
                HandleRawTransportResult(
                    hub,
                    conn,
                    rawLimiter.RegisterProtocolViolation(),
                    $"Encrypted message used unknown security level {packed.Level}.");
                return false;
            }

            byte[] decryptedData;
            if (encrypted)
            {
                if (!AuthTransportCrypto.IsVersion2Envelope(packedData))
                {
                    HandleRawTransportResult(
                        hub,
                        conn,
                        rawLimiter.RegisterProtocolViolation(),
                        "Rejected a legacy or malformed directional AEAD v2 envelope for application protocol v4.");
                    return false;
                }

                try
                {
                    if (!AuthenticatedTransportSessions.TryDecryptFromClient(hub, packedData, out decryptedData))
                    {
                        HandleRawTransportResult(
                            hub,
                            conn,
                            rawLimiter.RegisterProtocolViolation(),
                            "Encrypted message had no directional authenticated transport session.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    HandleRawTransportResult(
                        hub,
                        conn,
                        rawLimiter.RegisterProtocolViolation(),
                        $"Encrypted message authentication failed (connId={conn?.connectionId ?? -1}, " +
                        $"bytes={packedData.Length}, error={ex.GetType().Name}).");
                    return false;
                }
            }
            else
            {
                decryptedData = packedData;
            }

            if (decryptedData == null || decryptedData.Length == 0)
            {
                HandleRawTransportResult(
                    hub,
                    conn,
                    rawLimiter.RegisterProtocolViolation(),
                    $"Decoded encrypted payload was {(decryptedData == null ? "null" : "empty")}.");
                return false;
            }

            channel = (EncryptedChannelManager.EncryptedChannel)decryptedData[0];

            if (!encrypted && RequiresEncryption(channel))
            {
                HandleRawTransportResult(
                    hub,
                    conn,
                    rawLimiter.RegisterProtocolViolation(),
                    $"Message on channel {channel} was sent without required encryption.");
                return false;
            }

            // Anomaly custom messages -- separate dispatch. Wire is
            // [0xFE][counter][hash][payload]; AnomalyMessaging.OnReceived
            // parses the counter/hash and applies replay protection.
            if ((byte)channel == AnomalyChannel.ChannelId)
            {
                var anomalyPayload = new byte[decryptedData.Length - 1];
                Array.Copy(decryptedData, 1, anomalyPayload, 0, anomalyPayload.Length);
                AnomalyMessaging.OnReceived(hub, anomalyPayload, encrypted, false);
                return false;
            }

            if (decryptedData.Length < EncryptedMessageHeaderSize)
            {
                HandleRawTransportResult(
                    hub,
                    conn,
                    rawLimiter.RegisterProtocolViolation(),
                    $"Decoded payload was too short ({decryptedData.Length} bytes, " +
                    $"channel={channel}, level={packed.Level}).");
                return false;
            }

            int genericPayloadBytes = decryptedData.Length - EncryptedMessageHeaderSize;
            uint genericRateKey = GenericTransportRateKeyPrefix | (byte)channel;
            var genericRateResult = AnomalyReplayTable.GetRateLimiterOrCreate(hub)
                .TryAccept(genericRateKey, genericPayloadBytes);
            if (!genericRateResult.Allowed)
            {
                if (genericRateResult.ShouldDisconnect)
                {
                    QuarantineAndDisconnect(hub, conn, "generic-channel-rate-limit");
                    OffThreadLogger.Error(
                        $"Disconnected {hub.LoggedNameFromRefHub()} after sustained generic " +
                        "encrypted-channel traffic exceeded bounded limits.");
                }
                if (genericRateResult.ShouldLog)
                {
                    OffThreadLogger.Warn(
                        $"Rate-limited generic encrypted channel {channel} from {hub.LoggedNameFromRefHub()} " +
                        $"({genericPayloadBytes} bytes, drops={genericRateResult.DroppedInWindow}).");
                }
                return false;
            }

            counter = BitConverter.ToUInt32(decryptedData, 1);
            content = new byte[decryptedData.Length - EncryptedMessageHeaderSize];
            Array.Copy(decryptedData, EncryptedMessageHeaderSize, content, 0, content.Length);
            message = new EncryptedChannelManager.EncryptedMessage(channel, content, counter);

            if (encrypted)
            {
                if (counter == uint.MaxValue)
                {
                    QuarantineAndDisconnect(hub, conn, "receive-counter-exhaustion");
                    OffThreadLogger.Error(
                        $"Disconnected {hub.LoggedNameFromRefHub()}: encrypted receive counter is exhausted.");
                    return false;
                }

                if (!EncryptedTransportCounterGuard.CanAccept(
                        hub.encryptedChannelManager._rxCounter,
                        counter))
                {
                    HandleRawTransportResult(
                        hub,
                        conn,
                        rawLimiter.RegisterProtocolViolation(),
                        $"Rejected encrypted counter {counter}; high-water mark is " +
                        $"{hub.encryptedChannelManager._rxCounter}.");
                    return false;
                }

                hub.encryptedChannelManager._rxCounter = counter;
            }
        }


        if (!EncryptedChannelManager.ServerChannelHandlers.TryGetValue(channel, out var handler))
        {
            HandleRawTransportResult(
                hub,
                conn,
                rawLimiter.RegisterProtocolViolation(),
                $"No server handler is registered for encrypted channel {channel}.");
            return false;
        }

        foreach (Delegate subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EncryptedChannelManager.EncryptedMessageServerHandler)subscriber)
                    .Invoke(hub, message, packed.Level);
            }
            catch (Exception ex)
            {
                try
                {
                    OffThreadLogger.Error(
                        $"Exception while handling encrypted message on channel {channel} " +
                        $"(server, running an isolated handler): {ex}");
                }
                catch { }
            }
        }
        return false;
    }

    private static bool RequiresEncryption(EncryptedChannelManager.EncryptedChannel channel)
    {
        return channel == EncryptedChannelManager.EncryptedChannel.RemoteAdmin ||
               channel == EncryptedChannelManager.EncryptedChannel.AdminChat ||
               (byte)channel == AnomalyChannel.ChannelId;
    }

    private static void ExhaustTransportCounter(EncryptedChannelManager manager)
    {
        ReferenceHub hub = null;
        try
        {
            if (manager != null)
                ReferenceHub.TryGetHub(manager.connectionToClient, out hub);
        }
        catch { }
        FailClosedOutgoingTransport(
            manager,
            hub,
            "send-counter-exhaustion",
            "Encrypted send counter was exhausted; transport was revoked before counter reuse.");
    }

    private static void FailClosedOutgoingTransport(
        EncryptedChannelManager manager,
        ReferenceHub hub,
        string context,
        string diagnostic)
    {
        if (hub != null)
        {
            try { Plugin.QuarantineAuthenticatedPlayer(hub, context); }
            catch { }
        }

        Exception disconnectFailure = null;
        try { manager?.connectionToClient?.Disconnect(); }
        catch (Exception exception) { disconnectFailure = exception; }

        try { OffThreadLogger.Warn(diagnostic); }
        catch { }
        if (disconnectFailure != null)
        {
            try
            {
                OffThreadLogger.Error(
                    $"Failed to disconnect after {context}: {disconnectFailure.GetType().Name}: {disconnectFailure.Message}");
            }
            catch { }
        }
    }

    private static void HandleRawTransportResult(
        ReferenceHub hub,
        NetworkConnection conn,
        AnomalyRateLimitResult result,
        string message)
    {
        if (result.ShouldDisconnect)
        {
            QuarantineAndDisconnect(hub, conn, "raw-transport-rate-limit");
            OffThreadLogger.Error(
                $"Disconnected {hub.LoggedNameFromRefHub()} after sustained invalid or excessive pre-decrypt traffic.");
        }
        if (result.ShouldLog)
        {
            OffThreadLogger.Warn(
                $"{message} Peer={hub.LoggedNameFromRefHub()}, drops={result.DroppedInWindow}.");
        }
    }

    private static void QuarantineAndDisconnect(
        ReferenceHub hub,
        NetworkConnection conn,
        string context)
    {
        Plugin.QuarantineAuthenticatedPlayer(hub, context);
        try { conn?.Disconnect(); }
        catch (Exception ex)
        {
            OffThreadLogger.Error($"Failed to disconnect {hub?.LoggedNameFromRefHub()} after {context}: {ex}");
        }
    }
}
