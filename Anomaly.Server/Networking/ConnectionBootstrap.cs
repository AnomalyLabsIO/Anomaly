using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Anomaly.Server.Auth;
using Anomaly.Server.Internal.Map;
using Anomaly.Server.Utils;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using HarmonyLib;
using Mirror;
using Mirror.LiteNetLib4Mirror;
using NetworkManagerUtils.Dummies;

namespace Anomaly.Server.Networking;

/// <summary>Claims accepted peer ownership before Mirror's seed and scene callbacks.</summary>
internal static class ConnectionBootstrap
{
    private static readonly Dictionary<NetworkConnectionToClient, ulong> SentGenerations =
        new Dictionary<NetworkConnectionToClient, ulong>();
    private static bool _initialized;
    private static ConditionalWeakTable<NetworkConnectionToClient, object> Rejected = new ConditionalWeakTable<NetworkConnectionToClient, object>();

    internal static void Init()
    {
        if (_initialized) return;
        FacilityLayoutRuntime.SnapshotFrozen += OnSnapshotFrozen;
        FacilityLayoutRuntime.GenerationStarted += ConnectionContentBootstrap.OnGenerationStarted;
        FacilityLayoutRuntime.GenerationRejected += OnGenerationRejected;
        _initialized = true;
    }

    internal static void Disable()
    {
        if (_initialized) FacilityLayoutRuntime.SnapshotFrozen -= OnSnapshotFrozen;
        if (_initialized) FacilityLayoutRuntime.GenerationStarted -= ConnectionContentBootstrap.OnGenerationStarted;
        if (_initialized) FacilityLayoutRuntime.GenerationRejected -= OnGenerationRejected;
        _initialized = false;
        SentGenerations.Clear();
        ConnectionContentBootstrap.Reset();
        Rejected = new ConditionalWeakTable<NetworkConnectionToClient, object>();
    }

    private static void OnSnapshotFrozen(FacilitySnapshotMessage snapshot)
    {
        foreach (var connection in AuthenticatedTransportSessions.SnapshotConnections())
            TrySendCurrentSnapshot(connection);
    }

    private static void OnGenerationRejected(Exception exception)
    {
        foreach (var connection in AuthenticatedTransportSessions.SnapshotConnections())
            Reject(connection, "Facility generation rejected: " + exception.Message);
    }

    // True includes a pending freeze. The client holds its seed until the snapshot is available.
    internal static bool TrySendCurrentSnapshot(NetworkConnectionToClient connection)
    {
        if (connection is LocalConnectionToClient || connection is DummyNetworkConnection) return true;
        if (FacilityLayoutRuntime.GenerationFailed)
        {
            Reject(connection, "Facility generation is unavailable after a generation failure.");
            return false;
        }
        if (!AuthenticatedTransportSessions.IsActiveConnection(connection)) return false;
        if (!FacilityLayoutRuntime.TryGetSnapshot(out var snapshot)) return true;
        if (SentGenerations.TryGetValue(connection, out ulong generation) && generation == snapshot.GenerationId)
            return true;
        try
        {
            if (!AnomalyMessaging.TrySendToConnection(connection, snapshot))
                throw new InvalidOperationException("Facility transport delivery was unavailable.");
            SentGenerations[connection] = snapshot.GenerationId;
            ConnectionContentBootstrap.OnSnapshotDelivered(connection, snapshot.GenerationId);
            return AuthenticatedTransportSessions.IsActiveConnection(connection);
        }
        catch (Exception exception)
        {
            Reject(connection, "Authenticated facility delivery failed: " + exception.Message);
            return false;
        }
    }

    private static bool Claim(NetworkConnectionToClient connection)
    {
        if (connection is LocalConnectionToClient || connection is DummyNetworkConnection) return true;
        if (FacilityLayoutRuntime.GenerationFailed)
        {
            Reject(connection, "Facility generation is unavailable after a generation failure.");
            return false;
        }
        PlayerStorage player = null;
        try
        {
            bool admitted = Plugin.TryCommitAuthenticatedAdmission(() =>
            {
                if (!LiteNetLib4MirrorServer.Peers.TryGetValue(connection.connectionId, out var peer) ||
                    peer == null || !AuthHandler.TryTakePlayerForPeer(peer.EndPoint, peer, out player))
                    throw new InvalidOperationException("Accepted peer authentication was unavailable.");
                AuthenticatedTransportSessions.RegisterConnection(connection, player);
            });
            if (!admitted) throw new InvalidOperationException("Authenticated admission is closed.");
            ConnectionContentBootstrap.Begin(connection);
            return TrySendCurrentSnapshot(connection);
        }
        catch (Exception exception)
        {
            if (player != null)
            {
                EnrollmentSessionRegistry.Cancel(player.UserId, player.EnrollmentReservation);
                player.ClearSecrets();
            }
            Reject(connection, "Connection bootstrap failed: " + exception.Message);
            return false;
        }
    }

    internal static void Reject(NetworkConnectionToClient connection, string reason)
    {
        if (connection == null) return;
        lock (Rejected)
        {
            if (Rejected.TryGetValue(connection, out _)) return;
            Rejected.Add(connection, new object());
        }
        ConnectionContentBootstrap.MarkRejected(connection);
        AuthenticatedTransportSessions.QuarantineConnection(connection);
        SentGenerations.Remove(connection);
        try { ConnectionContentBootstrap.Cleanup(connection); }
        catch (Exception exception) { try { OffThreadLogger.Error("Rejected content cleanup failed: " + exception.Message); } catch { } }
        try
        {
            // Use the game's disconnect payload, not a final Mirror message:
            // queued batches can be discarded by the disconnect below. The
            // stock client decodes this reason before its main-thread teardown.
            // Bound text so UTF-8/base64 framing fits the stock initial MTU.
            const int maximumReasonCharacters = 160;
            string disconnectReason = reason ?? "Required connection content was rejected.";
            if (disconnectReason.Length > maximumReasonCharacters)
                disconnectReason = disconnectReason.Substring(0, maximumReasonCharacters - 3) + "...";
            // The stock helper clears this internal shared field only on a
            // normal return. Preserve another caller's value even if an active
            // transport throws while disconnecting this connection.
            var reasonField = AccessTools.Field(typeof(LiteNetLib4MirrorServer), "DisconnectMessage");
            if (NetworkManager.singleton is LiteNetLib4MirrorNetworkManager manager && reasonField != null)
            {
                var previousReason = reasonField.GetValue(null);
                try { manager.DisconnectConnection(connection, disconnectReason); }
                finally { reasonField.SetValue(null, previousReason); }
            }
            else
                connection.Disconnect();
            // A failed claim never enters NetworkServer.connections, so Mirror
            // cannot deliver its normal OnServerDisconnect callback. Once the
            // disconnect is initiated, release that connection-only marker too.
            AuthenticatedTransportSessions.RemoveConnection(connection);
        }
        catch { }
        try { OffThreadLogger.Error(reason); }
        catch { }
    }

    [HarmonyPatch(typeof(NetworkServer), "OnConnected")]
    private static class ConnectedPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(NetworkConnectionToClient conn)
        {
            try
            {
                // Native registration normally waits for the host hub's Start().
                // Bootstrap traffic must already have its patched receive boundary before any remote hub exists.
                NetworkServer.ReplaceHandler<EncryptedChannelManager.EncryptedMessageOutside>(
                    EncryptedChannelManager.ServerReceivePackedMessage);
                return Claim(conn);
            }
            catch (Exception exception)
            {
                Reject(conn, "Connection receive bootstrap failed: " + exception.Message);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnServerDisconnect))]
    private static class DisconnectedPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NetworkConnectionToClient conn)
        {
            SentGenerations.Remove(conn);
            AuthenticatedTransportSessions.RemoveConnection(conn);
            ConnectionContentBootstrap.Cleanup(conn);
        }
    }
}
