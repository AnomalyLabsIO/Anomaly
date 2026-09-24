using System;
using Anomaly.Server.Auth;
using System.Security.Cryptography;
using System.Text;
using Anomaly.Server.Utils;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using CentralAuth;
using GameCore;
using HarmonyLib;
using LabApi.Events;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Features.Console;
using LiteNetLib;
using LiteNetLib.Utils;
using Mirror.LiteNetLib4Mirror;

namespace Anomaly.Server.Patches;

[HarmonyPatch(typeof(CustomLiteNetLib4MirrorTransport), nameof(CustomLiteNetLib4MirrorTransport.ProcessConnectionRequest))]
internal static class ProcessConnectionRequestPatch
{
    private const byte HandshakeResponseCode = 100;
    private const byte RetryCookieResponseCode = 101;
    private const long MaximumPreauthFutureSeconds = 60;
    private const long MaximumPreauthPastSkewSeconds = 5;

    [HarmonyPrefix]
    public static bool OnConnectionRequest(ConnectionRequest request)
    {
        // Thin wrapper - no Anomaly.Shared types referenced here, so JIT cannot fail
        // due to missing assemblies. If OnConnectionRequestInner fails to JIT, we catch it.
        try
        {
            return OnConnectionRequestInner(request);
        }
        catch (Exception ex)
        {
            OffThreadLogger.Error($"ProcessConnectionRequest prefix exception (likely JIT failure): {ex}");
            // Reject rather than fall through to vanilla. Vanilla preauth on an
            // Anomaly server runs in offline mode and would accept unauthenticated
            // clients, so silent fall-through is an auth bypass.
            TryRejectOnException(request);
            return false;
        }
    }

    private static void TryRejectOnException(ConnectionRequest request)
    {
        try
        {
            RequestWriter.Reset();
            RequestWriter.Put((byte)RejectionReason.Error);
            request.RejectForce(RequestWriter);
        }
        catch { /* best effort — connection is getting dropped either way */ }
    }

    private static bool OnConnectionRequestInner(ConnectionRequest request)
    {
        if (!Plugin.IsAdmissionOpen)
        {
            // A partially initialized or restoration-failed plugin must consume
            // the request. Falling through while OnlineMode is false would hand
            // the peer to vanilla's unauthenticated offline path.
            TryRejectOnException(request);
            return false;
        }

        OffThreadLogger.Info("Connection from " + request.RemoteEndPoint.Address);

        try
        {
            var ip = request.RemoteEndPoint.Address.ToString();

            #region RequestType
            if (!request.Data.TryGetByte(out var requestType))
            {
                OffThreadLogger.Warn("Cannot get request type.");
                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.InvalidToken);
                request.RejectForce(RequestWriter);
                return false;
            }
            //Vanilla would use 0 and central server 1
            if(requestType < 3)
            {
                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.VersionMismatch);
                request.RejectForce(RequestWriter);
                return false;
            }
            #endregion

            #region GameVersion
            byte cBackwardRevision = 0;
            if (!request.Data.TryGetByte(out var cMajor)
                || !request.Data.TryGetByte(out var cMinor)
                || !request.Data.TryGetByte(out var cRevision)
                || !request.Data.TryGetBool(out var flag)
                || (flag && !request.Data.TryGetByte(out cBackwardRevision)))
            {
                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.VersionMismatch);
                request.RejectForce(RequestWriter);
                return false;
            }

            if (!GameCore.Version.CompatibilityCheck(GameCore.Version.Major, GameCore.Version.Minor, GameCore.Version.Revision,
                cMajor, cMinor, cRevision, flag, cBackwardRevision))
            {
                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.VersionMismatch);
                request.RejectForce(RequestWriter);
                return false;
            }
            #endregion

            #region DelayConnections
            if (CustomLiteNetLib4MirrorTransport.DelayConnections)
            {
                CustomLiteNetLib4MirrorTransport.PreauthDisableIdleMode();
                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.Delay);
                RequestWriter.Put(CustomLiteNetLib4MirrorTransport.DelayTime);

                if (CustomLiteNetLib4MirrorTransport.DelayVolume < 255)
                    CustomLiteNetLib4MirrorTransport.DelayVolume += 1;

                if (CustomLiteNetLib4MirrorTransport.DelayVolume < CustomLiteNetLib4MirrorTransport.DelayVolumeThreshold)
                {
                    if (CustomLiteNetLib4MirrorTransport.DisplayPreauthLogs)
                        OffThreadLogger.Debug(string.Format("Delayed connection incoming from endpoint {0} by {1} seconds.", request.RemoteEndPoint, CustomLiteNetLib4MirrorTransport.DelayTime));
                    
                    request.Reject(RequestWriter);
                }
                else
                {
                    if (CustomLiteNetLib4MirrorTransport.DisplayPreauthLogs)
                        OffThreadLogger.Debug(string.Format("Force delayed connection incoming from endpoint {0} by {1} seconds.", request.RemoteEndPoint, CustomLiteNetLib4MirrorTransport.DelayTime));

                    request.RejectForce(RequestWriter);
                }

                return false;
            }
            #endregion

            //TODO: Reimplement Proxies

            #region CheckTimeStamp
            if (!request.Data.TryGetLong(out var timeStamp))
            {
                OffThreadLogger.Warn("Cannot get timestamp.");
                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.InvalidToken);
                request.RejectForce(RequestWriter);
                return false;
            }
            var currentTimestamp = TimeBehaviour.CurrentUnixTimestamp;
            if (timeStamp < currentTimestamp - MaximumPreauthPastSkewSeconds
                || timeStamp > currentTimestamp + MaximumPreauthFutureSeconds)
            {
                RequestRejected();
                if(LogRejection)
                {
                    OffThreadLogger.Warn(
                        $"Rejected preauthentication timestamp from {ip}: expiry={timeStamp}, serverNow={currentTimestamp}.");
                }
                RequestWriter.Reset();
                RequestWriter.Put((byte)(timeStamp < currentTimestamp
                    ? RejectionReason.ExpiredAuth
                    : RejectionReason.InvalidToken));
                request.RejectForce(RequestWriter);
                CustomLiteNetLib4MirrorTransport.ResetIdleMode();
                return false;
            }
            #endregion

            #region AnomalyAuth
            if (!PreauthGate.TryAcceptRequest(request.RemoteEndPoint.Address))
            {
                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.RateLimit);
                request.RejectForce(RequestWriter);
                CustomLiteNetLib4MirrorTransport.ResetIdleMode();
                return false;
            }

            if (PasswordStore.IsRequired && PasswordStore.IsBlocked(ip))
            {
                OffThreadLogger.Warn($"Rejecting {ip}: too many failed Anomaly password attempts.");
                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.RateLimit);
                request.RejectForce(RequestWriter);
                CustomLiteNetLib4MirrorTransport.ResetIdleMode();
                return false;
            }

            if(!request.Data.TryGetBytesWithLength(out var authData))
            {
                OffThreadLogger.Warn($"Failed to read auth data from {ip}");
                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.Error);
                request.RejectForce(RequestWriter);
                return false;
            }

            var userId = string.Empty;
            var name = string.Empty;
            Connection authenticatedConnection = null;
            string authenticatedServerIdentifier = null;

            switch (requestType)
            {
                case 3: // Handshake/Preauth
                    ClientHandshake clientHandshake;
                    try
                    {
                        clientHandshake = ClientHandshake.Decode(authData);
                    }
                    catch (AnomalyProtocolVersionMismatchException pv)
                    {
                        OffThreadLogger.Warn($"Rejecting {ip}: {pv.Message}");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.VersionMismatch);
                        request.RejectForce(RequestWriter);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        OffThreadLogger.Warn($"Rejecting malformed phase-one auth from {ip}: {ex.Message}");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.InvalidToken);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    if (!clientHandshake.MatchesGameVersion(
                            cMajor,
                            cMinor,
                            cRevision,
                            flag,
                            cBackwardRevision))
                    {
                        OffThreadLogger.Warn(
                            $"Rejecting {ip}: signed phase-one game version did not match the outer preauth fields.");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.VersionMismatch);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    if (!PreauthGate.ValidateRetryCookie(request.RemoteEndPoint.Address, clientHandshake))
                    {
                        var retryCookie = PreauthGate.CreateRetryCookie(request.RemoteEndPoint.Address, clientHandshake);
                        RequestWriter.Reset();
                        RequestWriter.Put(RetryCookieResponseCode);
                        RequestWriter.PutBytesWithLength(retryCookie);
                        request.RejectForce(RequestWriter);
                        CustomLiteNetLib4MirrorTransport.PreauthDisableIdleMode();
                        return false;
                    }

                    try
                    {
                        AuthCrypto.ReadPublic(clientHandshake.identityPublic);
                        var anySessionKeyByte = 0;
                        for (var i = 0; i < clientHandshake.sessionPublic.Length; i++)
                            anySessionKeyByte |= clientHandshake.sessionPublic[i];
                        if (anySessionKeyByte == 0)
                            throw new CryptographicException("Client X25519 public key is all zero.");
                    }
                    catch (Exception ex)
                    {
                        OffThreadLogger.Warn($"Rejecting invalid client key material from {ip}: {ex.Message}");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.InvalidToken);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    if (!clientHandshake.TryValidateIdentityRotationChain(
                            out var enrollmentIdentityPublic,
                            out var rotationError))
                    {
                        OffThreadLogger.Warn(
                            $"Rejecting invalid client identity rotation from {ip}: {rotationError}");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.InvalidToken);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    try
                    {
                        userId = ServerIdentityStore.GetEnrollmentId(enrollmentIdentityPublic);
                    }
                    finally
                    {
                        Array.Clear(enrollmentIdentityPublic, 0, enrollmentIdentityPublic.Length);
                    }

                    if (!PasswordStore.TryCreateChallenge(
                            request.RemoteEndPoint.Address,
                            out var passwordChallenge,
                            out var passwordChallengeError))
                    {
                        OffThreadLogger.Error($"Rejecting {ip}: {passwordChallengeError}");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.Error);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    if (!AuthHandler.TryReserveConnection(
                            clientHandshake,
                            request.RemoteEndPoint.Address,
                            passwordChallenge,
                            out var serverIden,
                            out var reservedConnection))
                    {
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.RateLimit);
                        request.RejectForce(RequestWriter);
                        return false;
                    }
                    reservedConnection.EnrollmentId = userId;

                    byte[] persistentIdentity = null;
                    try
                    {
                        persistentIdentity = ServerIdentityStore.GetIdentity();
                        var serverEnv = ServerAuthSession.Generate(persistentIdentity);
                        var serverHandshake = serverEnv.CreateHandshake(
                            clientHandshake,
                            serverIden,
                            passwordChallenge,
                            ServerIdentityStore.GetActiveRotationRecord());
                        reservedConnection.ServerEnv = serverEnv;
                        reservedConnection.ServerHandshake = serverHandshake;
                        var serverHandshakeBytes = serverHandshake.Encode();

                        RequestWriter.Reset();
                        RequestWriter.Put(HandshakeResponseCode);
                        RequestWriter.PutBytesWithLength(serverHandshakeBytes);
                        RequestWriter.Put(serverIden);
                        WritePasswordChallenge(RequestWriter, passwordChallenge);
                        request.RejectForce(RequestWriter);
                    }
                    catch
                    {
                        AuthHandler.RemoveConnection(serverIden);
                        throw;
                    }
                    finally
                    {
                        if (persistentIdentity != null)
                            Array.Clear(persistentIdentity, 0, persistentIdentity.Length);
                    }

                    OffThreadLogger.Debug("Got Handshake from user " + userId + " from ip " + ip);
                    CustomLiteNetLib4MirrorTransport.PreauthDisableIdleMode();
                    return false;

                case 4: // Connect
                    ClientLoginAttempt attempt;
                    try
                    {
                        attempt = ClientLoginAttempt.Decode(authData);
                    }
                    catch (Exception ex)
                    {
                        OffThreadLogger.Warn($"Rejecting malformed phase-two auth from {ip}: {ex.Message}");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.InvalidToken);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    if (!request.Data.TryGetString(out var serverIdentifier)
                        || string.IsNullOrEmpty(serverIdentifier)
                        || serverIdentifier.Length > AuthTranscript.MaximumServerIdentifierBytes)
                    {
                        OffThreadLogger.Warn($"Failed to read server identifier from {ip} during login phase");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.Error);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    if (!request.Data.TryGetString(out name)
                        || string.IsNullOrWhiteSpace(name)
                        || Encoding.UTF8.GetByteCount(name) > AuthTranscript.MaximumDisplayNameBytes)
                    {
                        OffThreadLogger.Warn($"Rejected invalid username from {ip} during login phase");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.InvalidToken);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    if(!AuthHandler.TryConsumeConnection(
                            serverIdentifier,
                            request.RemoteEndPoint.Address,
                            out var connection))
                    {
                        OffThreadLogger.Warn($"No matching handshake session for identifier from {ip}. Session may have expired.");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.Error);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    if (!connection.Handshake.MatchesGameVersion(
                            cMajor,
                            cMinor,
                            cRevision,
                            flag,
                            cBackwardRevision))
                    {
                        OffThreadLogger.Warn(
                            $"Rejecting {ip}: phase-two game version differed from the signed phase-one transcript.");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.VersionMismatch);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    if (!connection.ServerEnv.Validate(
                            connection.Handshake,
                            connection.ServerHandshake,
                            serverIdentifier,
                            connection.PasswordChallenge,
                            name,
                            attempt,
                            out var passwordProof))
                    {
                        OffThreadLogger.Warn($"Login attempt validation failed for {ip}. Signature mismatch.");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.InvalidToken);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    if (connection.PasswordRequired && !PasswordStore.VerifyProof(connection, serverIdentifier, passwordProof))
                    {
                        OffThreadLogger.Warn(
                            $"Password proof validation failed for {ip} "
                            + $"({connection.EnrollmentId})." );
                        PasswordStore.RecordFailure(ip);
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.InvalidToken);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    if (connection.PasswordRequired)
                        PasswordStore.ClearFailures(ip);

                    userId = connection.EnrollmentId;
                    authenticatedConnection = connection;
                    authenticatedServerIdentifier = serverIdentifier;

                    OffThreadLogger.Debug($"Verified {name}({userId}) - " + ip);
                    break;

                default:
                    OffThreadLogger.Warn($"Unknown request type {requestType} from {ip}");
                    RequestWriter.Reset();
                    RequestWriter.Put((byte)RejectionReason.VerificationRejected);
                    request.RejectForce(RequestWriter);
                    return false;
            }
            #endregion

            #region Banned
            var banInfo = BanHandler.QueryBan(userId, ip);
            if(banInfo.Key != null || banInfo.Value != null)
            {
                var ban = banInfo.Key ?? banInfo.Value;
                RequestRejected();

                if(LogRejection)
                {
                    var msg = $"{(banInfo.Key == null ? "Player" : "Banned Player")} " +
                        $"{userId} tried to connect from {(banInfo.Value == null ? "" : "banned")} endpoint {ip}";
                    OffThreadLogger.Debug(msg);
                }

                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.Banned);
                RequestWriter.Put(ban.Expires);
                RequestWriter.Put(ban.Reason);
                request.Reject(RequestWriter);
                CustomLiteNetLib4MirrorTransport.ResetIdleMode();
                return false;
            }

            #endregion

            #region RateLimit
            //Maybe make this earlier and let it use the ip instead of userid
            if (CustomLiteNetLib4MirrorTransport.UserRateLimiting)
            {
                if (CustomLiteNetLib4MirrorTransport.UserRateLimit.ContainsKey(userId))
                {
                    RequestRejected();
                    if (LogRejection)
                    {
                        OffThreadLogger.Debug($"Incoming connection from {userId} ({ip}) rejected due to exceeding the rate limit");
                    }

                    RequestWriter.Reset();
                    RequestWriter.Put((byte)RejectionReason.RateLimit);
                    request.RejectForce(RequestWriter);
                    CustomLiteNetLib4MirrorTransport.ResetIdleMode();
                    return false;
                }
                CustomLiteNetLib4MirrorTransport.UserRateLimit.TryAdd(userId, 0);
            }
            #endregion

            #region Whitelist
            if (ServerConsole.WhiteListEnabled && !WhiteList.IsWhitelisted(userId))
            {
                if (CustomLiteNetLib4MirrorTransport.DisplayPreauthLogs)
                {
                    OffThreadLogger.Debug($"Player {userId} tried joined from endpoint {ip}, but is not whitelisted");
                }

                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.NotWhitelisted);
                request.Reject(RequestWriter);
                CustomLiteNetLib4MirrorTransport.ResetIdleMode();
                return false;
            }
            #endregion

            var preauthFlags = CentralAuthPreauthFlags.None;

            #region Slots
            // Use the game's user-aware fullness check so the authenticated
            // enrollment ID, configured reserved-slot list, and current slot
            // limits are evaluated by the same path as vanilla preauth.
            bool serverFull = ServerFullnessPolicy.IsFull(
                userId,
                preauthFlags,
                CustomLiteNetLib4MirrorTransport.IsServerFull);
            if (serverFull)
            {
                OffThreadLogger.Debug($"Player {userId} ({ip}) rejected - server full");
                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.ServerFull);
                request.Reject(RequestWriter);
                CustomLiteNetLib4MirrorTransport.ResetIdleMode();
                return false;
            }
            #endregion

            #region LabAPI
            int position = request.Data.Position;
            PlayerPreAuthenticatingEventArgs ev = new PlayerPreAuthenticatingEventArgs(
                !serverFull,
                userId,
                request.RemoteEndPoint.Address.ToString(),
                0L,
                preauthFlags,
                string.Empty,
                (byte[]) null,
                request,
                position);
            LabApi.Events.Handlers.PlayerEvents.OnPreAuthenticating(ev);
            if (!ev.IsAllowed)
            {
                OffThreadLogger.Debug($"Player {userId} ({ip}) rejected by LabAPI plugin");
                if (ev.CustomReject != null)
                {
                    if (ev.ForceReject)
                        request.RejectForce(ev.CustomReject);
                    else
                        request.Reject(ev.CustomReject);
                }
                else
                {
                    CustomLiteNetLib4MirrorTransport.RequestWriter.Reset();
                    CustomLiteNetLib4MirrorTransport.RequestWriter.Put((byte)RejectionReason.Error);
                    request.RejectForce(CustomLiteNetLib4MirrorTransport.RequestWriter);
                }
                return false;
            }
            else if (ev.CanJoin)
            {
                var player = new Server.Auth.PlayerStorage
                {
                    NickName = name,
                    UserId = userId,
                };
                using (var keys = authenticatedConnection.ServerEnv.DeriveSessionKeys(
                           authenticatedConnection.Handshake,
                           authenticatedConnection.ServerHandshake,
                           authenticatedServerIdentifier,
                           authenticatedConnection.PasswordChallenge))
                {
                    player.ClientToServerKey = (byte[])keys.ClientToServerKey.Clone();
                    player.ServerToClientKey = (byte[])keys.ServerToClientKey.Clone();
                    player.SessionId = (byte[])keys.SessionId.Clone();
                    player.TranscriptHash = (byte[])keys.TranscriptHash.Clone();
                }

                if (!Plugin.IsAdmissionOpen)
                {
                    player.ClearSecrets();
                    TryRejectOnException(request);
                    return false;
                }

                if (!AcceptedClientIdentityStore.TryValidateAndCommit(
                        userId,
                        authenticatedConnection.Handshake,
                        out var acceptedIdentityError))
                {
                    player.ClearSecrets();
                    OffThreadLogger.Warn(
                        $"Rejected client identity progression for {userId} ({ip}): {acceptedIdentityError}");
                    RequestWriter.Reset();
                    RequestWriter.Put((byte)RejectionReason.InvalidToken);
                    request.RejectForce(RequestWriter);
                    return false;
                }

                player.EnrollmentReservation = EnrollmentSessionRegistry.Reserve(
                    userId,
                    PlayerAuthenticationManager.AllowSameAccountJoining);

                bool storedPlayer = false;
                bool admissionCommitted;
                try
                {
                    admissionCommitted = Plugin.TryCommitAuthenticatedAdmission(() =>
                    {
                        storedPlayer = AuthHandler.StorePlayer(request.RemoteEndPoint, player);
                        if (storedPlayer)
                            player.AcceptedPeer = request.Accept() ??
                                throw new InvalidOperationException("Accepted transport peer was unavailable.");
                    });
                }
                catch
                {
                    if (storedPlayer)
                        AuthHandler.RemoveStoredPlayer(request.RemoteEndPoint);
                    else
                    {
                        EnrollmentSessionRegistry.Cancel(userId, player.EnrollmentReservation);
                        player.ClearSecrets();
                    }
                    throw;
                }

                if (!admissionCommitted)
                {
                    EnrollmentSessionRegistry.Cancel(userId, player.EnrollmentReservation);
                    player.ClearSecrets();
                    TryRejectOnException(request);
                    return false;
                }
                if (!storedPlayer)
                {
                    OffThreadLogger.Warn($"Rejected {ip}: verified-player handoff capacity is full.");
                    RequestWriter.Reset();
                    RequestWriter.Put((byte)RejectionReason.RateLimit);
                    request.RejectForce(RequestWriter);
                    return false;
                }
                CustomLiteNetLib4MirrorTransport.PreauthDisableIdleMode();
                LabApi.Events.Handlers.PlayerEvents.OnPreAuthenticated(new PlayerPreAuthenticatedEventArgs(userId, request.RemoteEndPoint.Address.ToString(), 0L, CentralAuthPreauthFlags.None, string.Empty, (byte[]) null, request, position));
            }
            else
            {
                OffThreadLogger.Debug($"Player {userId} ({ip}) rejected by LabAPI - cannot join (server full)");
                CustomLiteNetLib4MirrorTransport.RequestWriter.Reset();
                CustomLiteNetLib4MirrorTransport.RequestWriter.Put((byte)RejectionReason.ServerFull);
                request.Reject(CustomLiteNetLib4MirrorTransport.RequestWriter);
                CustomLiteNetLib4MirrorTransport.ResetIdleMode();
                return false;
            }
            #endregion

            #region Finalize Accepting
            if (CustomLiteNetLib4MirrorTransport.UserIds.ContainsKey(request.RemoteEndPoint))
            {
                CustomLiteNetLib4MirrorTransport.UserIds[request.RemoteEndPoint].SetUserId(userId);
            }
            else
            {
                CustomLiteNetLib4MirrorTransport.UserIds[request.RemoteEndPoint] = new PreauthItem(userId);
            }

            /*var netPeer = request.Accept();

            //This is for proxy support later
            //if(realIp != null)
            if (false)
            {
                CustomLiteNetLib4MirrorTransport.RealIpAddresses[netPeer.Id] = ip;//realIP
            }*/

            /*if(Statistics.PeakPlayers.Total < LiteNetLib4MirrorCore.Host.ConnectedPeersCount)
            {
                Statistics.PeakPlayers = new Statistics.Peak(LiteNetLib4MirrorCore.Host.ConnectedPeersCount, DateTime.Now);
            }*/

            var msg2 = $"Player {userId} preauthenticated from endpoint {ip}.";
            OffThreadLogger.Info(msg2);

            CustomLiteNetLib4MirrorTransport.PreauthDisableIdleMode();
            #endregion

            return false;
        }
        catch (Exception ex)
        {
            try
            {
                OffThreadLogger.Error($"Exception in ProcessConnectionRequest: {ex}");
            }
            catch
            {
                System.Console.Error.WriteLine($"[Anomaly] ProcessConnectionRequest exception: {ex}");
            }
            TryRejectOnException(request);
        }
        return false;
    }

    private static NetDataWriter RequestWriter
    {
        get => CustomLiteNetLib4MirrorTransport.RequestWriter;
    }

    private static bool LogRejection
    {
        get => !CustomLiteNetLib4MirrorTransport.SuppressRejections && CustomLiteNetLib4MirrorTransport.DisplayPreauthLogs;
    }

    private static void RequestRejected()
    {
        CustomLiteNetLib4MirrorTransport.Rejected += 1u;
        if (CustomLiteNetLib4MirrorTransport.Rejected > CustomLiteNetLib4MirrorTransport.RejectionThreshold)
            CustomLiteNetLib4MirrorTransport.SuppressRejections = true;
    }

    private static void WritePasswordChallenge(NetDataWriter writer, PasswordChallenge challenge)
    {
        var required = challenge?.Required == true;
        writer.Put(required);
        if (!required)
            return;

        writer.PutBytesWithLength(challenge.Salt);
        writer.Put(challenge.Iterations);
        writer.PutBytesWithLength(challenge.Nonce);
    }
}
