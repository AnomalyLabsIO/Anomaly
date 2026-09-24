using System;
using System.Collections.Generic;
using System.IO;
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

    internal static void Init()
    {
        if (_initialized) return;
        FacilityLayoutRuntime.SnapshotFrozen += OnSnapshotFrozen;
        FacilityLayoutRuntime.GenerationRejected += OnGenerationRejected;
        _initialized = true;
    }

    internal static void Disable()
    {
        if (_initialized) FacilityLayoutRuntime.SnapshotFrozen -= OnSnapshotFrozen;
        if (_initialized) FacilityLayoutRuntime.GenerationRejected -= OnGenerationRejected;
        _initialized = false;
        SentGenerations.Clear();
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
            if (!AnomalyMessageRegistry.TryGetSendContract(snapshot, AnomalyMessageDirection.ServerToClient,
                    out uint hash, out var channel, out var policy))
                throw new InvalidOperationException("Facility snapshot did not match its registered contract.");
            byte[] bytes;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(AnomalyChannel.ChannelId);
                writer.Write(0U);
                writer.Write(hash);
                snapshot.Serialize(writer);
                writer.Flush();
                if (stream.Length - 9 > policy.MaxPayloadBytes)
                    throw new InvalidDataException("Facility snapshot exceeded its payload ceiling.");
                bytes = stream.ToArray();
            }
            var replay = AuthenticatedTransportSessions.GetReplayState(connection) ??
                throw new InvalidOperationException("Facility transport lost its replay state.");
            uint counter = replay.NextTxCounter(channel);
            if (counter == 0 || counter == uint.MaxValue)
                throw new InvalidOperationException("Facility transport counter exhausted.");
            Array.Copy(BitConverter.GetBytes(counter), 0, bytes, 1, sizeof(uint));
            if (!AuthenticatedTransportSessions.TryEncryptToConnection(connection, bytes, out var envelope))
                throw new InvalidOperationException("Facility transport lost its authentication keys.");
            var transport = LiteNetLib4MirrorTransport.Singleton;
            if (transport == null || !TransportPacketPolicy.FitsReportedChannel(channel, envelope.Length,
                    transport.GetMaxPacketSize, out _))
                throw new InvalidOperationException("Facility snapshot exceeded the deployed transport ceiling.");
            connection.Send(new EncryptedChannelManager.EncryptedMessageOutside(
                EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated, envelope), (int)channel);
            SentGenerations[connection] = snapshot.GenerationId;
            return true;
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

    private static void Reject(NetworkConnectionToClient connection, string reason)
    {
        AuthenticatedTransportSessions.QuarantineConnection(connection);
        SentGenerations.Remove(connection);
        try
        {
            connection?.Disconnect();
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
        private static bool Prefix(NetworkConnectionToClient conn) => Claim(conn);
    }

    [HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnServerDisconnect))]
    private static class DisconnectedPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NetworkConnectionToClient conn)
        {
            SentGenerations.Remove(conn);
            AuthenticatedTransportSessions.RemoveConnection(conn);
        }
    }
}
