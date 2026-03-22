using System;
using Anomaly.Server.Auth;
using Anomaly.Server.Utils;
using Anomaly.Shared.Auth;
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
public static class ProcessConnectionRequestPatch
{
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
            try
            {
                System.IO.File.AppendAllText("anomaly_debug.log",
                    $"[{DateTime.Now:HH:mm:ss.fff}] PREFIX EXCEPTION (likely JIT failure): {ex}\n");
            }
            catch { }
            return true; // fall through to original
        }
    }

    private static bool OnConnectionRequestInner(ConnectionRequest request)
    {
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
            if(TimeBehaviour.CurrentUnixTimestamp > timeStamp)
            {
                RequestRejected();
                if(LogRejection)
                {
                    OffThreadLogger.Warn("Player from endpoint " + ip + " sent expired preauthentication request.");
                    OffThreadLogger.Warn("Make sure that time and timezone set on server is correct. We recommend synchronizing the time.");
                }
                RequestWriter.Reset();
                RequestWriter.Put((byte)RejectionReason.ExpiredAuth);
                request.RejectForce(RequestWriter);
                CustomLiteNetLib4MirrorTransport.ResetIdleMode();
                return false;
            }
            #endregion

            #region AnomalyAuth
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
            var sharedKey = new byte[0];

            switch (requestType)
            {
                case 3: // Handshake/Preauth
                    var serverEnv = ServerAuthSession.Generate();
                    var clientHandshake = ClientHandshake.Decode(authData);
                    userId = clientHandshake.GetUserId();
                    var serverIden = AuthHandler.AddConnection(serverEnv, clientHandshake);

                    var serverHandshakeBytes = serverEnv.CreateHandshake().Encode();
                    OffThreadLogger.Debug($"ServerHandshake raw bytes ({serverHandshakeBytes.Length}): {BitConverter.ToString(serverHandshakeBytes, 0, Math.Min(serverHandshakeBytes.Length, 16))}...");
                    OffThreadLogger.Debug($"Client nonce[0..3]: {BitConverter.ToString(clientHandshake.nonce, 0, 4)}, sessionPublic[0..3]: {BitConverter.ToString(clientHandshake.sessionPublic, 0, 4)}");

                    RequestWriter.Reset();
                    RequestWriter.Put((byte)100);
                    RequestWriter.PutBytesWithLength(serverHandshakeBytes);
                    RequestWriter.Put(serverIden);
                    request.RejectForce(RequestWriter);

                    OffThreadLogger.Debug("Got Handshake from user " + userId + " from ip " + ip);
                    OffThreadLogger.Debug("Sending 100 response to client.");
                    CustomLiteNetLib4MirrorTransport.PreauthDisableIdleMode();
                    return false;

                case 4: // Connect
                    OffThreadLogger.Info("Attempting to decode auth request.");
                    var attempt = ClientLoginAttempt.Decode(authData);
                    OffThreadLogger.Info("Checking Server Identifier");
                    if (!request.Data.TryGetString(out var serverIdentifier))
                    {
                        OffThreadLogger.Warn($"Failed to read server identifier from {ip} during login phase");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.Error);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    OffThreadLogger.Info(serverIdentifier);

                    OffThreadLogger.Info("Checking connection");
                    if(!AuthHandler._connections.TryGetValue(serverIdentifier,out var connection))
                    {
                        OffThreadLogger.Warn($"No matching handshake session for identifier from {ip}. Session may have expired.");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.Error);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    OffThreadLogger.Info(connection.ToString());


                    OffThreadLogger.Info("Validating Attempt");
                    OffThreadLogger.Info($"{connection.Handshake}");
                    if (!connection.ServerEnv.Validate(connection.Handshake, attempt))
                    {
                        OffThreadLogger.Warn($"Login attempt validation failed for {ip}. Signature mismatch.");
                        AuthHandler._connections.Remove(serverIdentifier);
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.InvalidToken);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    OffThreadLogger.Info("Removing identifier");
                    AuthHandler._connections.Remove(serverIdentifier);

                    OffThreadLogger.Info("Getting username");
                    if (!request.Data.TryGetString(out name))
                    {
                        OffThreadLogger.Warn($"Failed to read username from {ip} during login phase");
                        RequestWriter.Reset();
                        RequestWriter.Put((byte)RejectionReason.Error);
                        request.RejectForce(RequestWriter);
                        return false;
                    }

                    OffThreadLogger.Info(name);

                    OffThreadLogger.Info("Get UserId");
                    userId = connection.Handshake.GetUserId();
                    OffThreadLogger.Info(userId);
                    OffThreadLogger.Info("Get Shared Key");
                    sharedKey = connection.ServerEnv.GetSharedSecret(connection.Handshake);
                    OffThreadLogger.Info(sharedKey.ToString());

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
                if (CustomLiteNetLib4MirrorTransport.UserRateLimit.Contains(userId))
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
                CustomLiteNetLib4MirrorTransport.UserRateLimit.Add(userId);
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

            #region Geoblocking
            var region = "";
            //TODO: Geoblocking
            //NW just sends the country along in the Request and that means it is really easy to manipulate
            if (false)
            {
                RequestRejected();
                if (LogRejection)
                {
                    OffThreadLogger.Debug($"Player {userId} ({ip}) tried to join from a blocked country {("INSERT COUNTRY HERE")}");
                }

                RequestWriter.Reset();
                RequestWriter.Put(9);
                request.Reject(RequestWriter);
                CustomLiteNetLib4MirrorTransport.ResetIdleMode();
            }
            #endregion

            #region Slots
            if (!HasSlot(userId))
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
            PlayerPreAuthenticatingEventArgs ev = new PlayerPreAuthenticatingEventArgs(!CustomLiteNetLib4MirrorTransport.IsServerFull(), string.Empty, request.RemoteEndPoint.Address.ToString(), 0L, CentralAuthPreauthFlags.None, string.Empty, (byte[]) null, request, position);
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
                request.Accept();
                CustomLiteNetLib4MirrorTransport.PreauthDisableIdleMode();
                LabApi.Events.Handlers.PlayerEvents.OnPreAuthenticated(new PlayerPreAuthenticatedEventArgs(string.Empty, request.RemoteEndPoint.Address.ToString(), 0L, CentralAuthPreauthFlags.None, string.Empty, (byte[]) null, request, position));
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

            AuthHandler._storedPlayers[request.RemoteEndPoint] = new Server.Auth.PlayerStorage
            {
                NickName = name,
                UserId = userId,
                SharedKey = sharedKey,
            };
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
        }
        return true;
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

    private static bool HasSlot(string userId)
    {
        //When the configured max slots are not full yet allow connections
        if (LiteNetLib4MirrorCore.Host.ConnectedPeersCount < CustomNetworkManager.slots)
            return true;

        //Reject the Connection when Mirror can't handle another Request
        if (LiteNetLib4MirrorCore.Host.ConnectedPeersCount == LiteNetLib4MirrorNetworkManager.singleton.maxConnections)
            return false;

        if(ConfigFile.ServerConfig.GetBool("use_reserved_slots",true) && ReservedSlot.HasReservedSlot(userId))
        {
            if (LiteNetLib4MirrorCore.Host.ConnectedPeersCount < CustomNetworkManager.slots + CustomNetworkManager.reservedSlots)
                return true;
        }

        return false;
    }
}
