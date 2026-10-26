using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Anomaly.Server.Auth;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Mirror;

namespace Anomaly.Server.Networking;

/// <summary>
/// Owns directional authenticated transport material after the preauth-to-Mirror
/// handoff. A native round restart can replace its hub without replacing keys or
/// counters; disconnect or failed admission removes and zeroes the key material.
/// </summary>
internal static class AuthenticatedTransportSessions
{
    private static readonly object Sync = new object();
    private static readonly ConcurrentDictionary<ReferenceHub, TransportSession> Sessions =
        new ConcurrentDictionary<ReferenceHub, TransportSession>();
    private static readonly ConcurrentDictionary<ReferenceHub, byte> QuarantinedHubs =
        new ConcurrentDictionary<ReferenceHub, byte>();
    private static readonly Dictionary<NetworkConnectionToClient, TransportSession> Connections =
        new Dictionary<NetworkConnectionToClient, TransportSession>();
    private static readonly HashSet<NetworkConnectionToClient> QuarantinedConnections =
        new HashSet<NetworkConnectionToClient>();

    internal static void RegisterConnection(NetworkConnectionToClient connection, PlayerStorage player)
    {
        if (connection == null) throw new ArgumentNullException(nameof(connection));
        if (player == null) throw new ArgumentNullException(nameof(player));
        lock (Sync)
        {
            if (Connections.ContainsKey(connection) || QuarantinedConnections.Contains(connection))
                throw new InvalidOperationException("This transport connection already owned a session.");
            Connections.Add(connection, new TransportSession(player) { Connection = connection });
            player.ClearSecrets();
        }
    }

    internal static bool TryAdoptConnection(ReferenceHub hub, out PlayerStorage player)
    {
        player = null;
        if (hub?.connectionToClient == null) return false;
        lock (Sync)
        {
            if (!Connections.TryGetValue(hub.connectionToClient, out var session) ||
                session.Hub != null || Sessions.ContainsKey(hub) || QuarantinedHubs.ContainsKey(hub))
                return false;
            session.Hub = hub;
            Sessions[hub] = session;
            player = session.Player;
            return true;
        }
    }

    internal static bool TryReleaseHubForRestart(ReferenceHub hub, bool allowSameAccountJoining)
    {
        if (hub == null) return false;
        lock (Sync)
        {
            if (!Sessions.TryGetValue(hub, out var session) || session.Connection == null ||
                !ReferenceEquals(session.Connection, hub.connectionToClient) ||
                !Connections.TryGetValue(session.Connection, out var owner) || !ReferenceEquals(owner, session))
                return false;
            if (!allowSameAccountJoining && !EnrollmentSessionRegistry.TrySuspendForRestart(
                    session.Player.UserId, hub, session.Player.EnrollmentReservation))
                return false;
            Sessions.TryRemove(hub, out _);
            session.Hub = null;
            return true;
        }
    }

    internal static AnomalyReplayState GetReplayState(ReferenceHub hub)
    {
        lock (Sync)
            return hub != null && Sessions.TryGetValue(hub, out var session) ? session.Replay : null;
    }

    internal static AnomalyReplayState GetReplayState(NetworkConnectionToClient connection)
    {
        lock (Sync)
            return connection != null && Connections.TryGetValue(connection, out var session) ? session.Replay : null;
    }

    internal static AnomalyInboundRateLimiter GetRateLimiter(ReferenceHub hub, bool raw)
    {
        lock (Sync)
            return hub != null && Sessions.TryGetValue(hub, out var session)
                ? raw ? session.RawLimiter : session.MessageLimiter : null;
    }

    internal static AnomalyInboundRateLimiter GetRateLimiter(NetworkConnectionToClient connection, bool raw)
    {
        lock (Sync)
            return connection != null && Connections.TryGetValue(connection, out var session)
                ? raw ? session.RawLimiter : session.MessageLimiter : null;
    }

    internal static bool TryEncryptToConnection(NetworkConnectionToClient connection, byte[] plaintext, out byte[] envelope)
    {
        lock (Sync)
        {
            if (connection == null || !Connections.TryGetValue(connection, out var session))
            {
                envelope = null;
                return false;
            }
            envelope = AuthTransportCrypto.Encrypt(session.ServerToClientKey, session.SessionId,
                session.TranscriptHash, AuthTransportDirection.ServerToClient, plaintext);
            return true;
        }
    }

    internal static bool IsActiveConnection(NetworkConnectionToClient connection)
    {
        lock (Sync) return connection != null && Connections.ContainsKey(connection);
    }

    internal static NetworkConnectionToClient[] SnapshotConnections()
    {
        lock (Sync)
        {
            var connections = new HashSet<NetworkConnectionToClient>(Connections.Keys);
            connections.UnionWith(QuarantinedConnections);
            var result = new NetworkConnectionToClient[connections.Count];
            connections.CopyTo(result);
            return result;
        }
    }

    internal static void RemoveConnection(NetworkConnectionToClient connection)
    {
        if (connection == null) return;
        lock (Sync)
        {
            if (Connections.TryGetValue(connection, out var session)) RemoveSession(session);
            QuarantinedConnections.Remove(connection);
        }
    }

    internal static void QuarantineConnection(NetworkConnectionToClient connection)
    {
        if (connection == null) return;
        lock (Sync)
        {
            if (Connections.TryGetValue(connection, out var session))
            {
                if (session.Hub != null) QuarantinedHubs.TryAdd(session.Hub, 0);
                RemoveSession(session);
            }
            QuarantinedConnections.Add(connection);
        }
    }

    private static void RemoveSession(TransportSession session)
    {
        if (session.Connection != null) Connections.Remove(session.Connection);
        if (session.Hub != null) Sessions.TryRemove(session.Hub, out _);
        session.Dispose();
    }

    public static void Register(ReferenceHub hub, PlayerStorage player)
    {
        if (hub == null)
            throw new ArgumentNullException(nameof(hub));
        if (player == null)
            throw new ArgumentNullException(nameof(player));

        var replacement = new TransportSession(player);
        lock (Sync)
        {
            if (Sessions.TryRemove(hub, out var previous))
                RemoveSession(previous);
            QuarantinedHubs.TryRemove(hub, out _);
            Sessions[hub] = replacement;
            replacement.Hub = hub;
            replacement.Connection = hub.connectionToClient;
            if (replacement.Connection != null)
            {
                QuarantinedConnections.Remove(replacement.Connection);
                Connections[replacement.Connection] = replacement;
            }
            ClearPlayerMaterial(player);
        }
    }

    public static bool TryEncryptToClient(ReferenceHub hub, byte[] plaintext, out byte[] envelope)
    {
        lock (Sync)
        {
            if (!Sessions.TryGetValue(hub, out var session))
            {
                envelope = null;
                return false;
            }

            envelope = AuthTransportCrypto.Encrypt(
                session.ServerToClientKey,
                session.SessionId,
                session.TranscriptHash,
                AuthTransportDirection.ServerToClient,
                plaintext);
            return true;
        }
    }

    internal static bool TryDecryptFromClient(NetworkConnectionToClient connection, byte[] envelope, out byte[] plaintext)
    {
        lock (Sync)
        {
            if (connection == null || !Connections.TryGetValue(connection, out var session))
            {
                plaintext = null;
                return false;
            }
            plaintext = AuthTransportCrypto.Decrypt(session.ClientToServerKey, session.SessionId,
                session.TranscriptHash, AuthTransportDirection.ClientToServer, envelope);
            return true;
        }
    }

    public static bool TryDecryptFromClient(ReferenceHub hub, byte[] envelope, out byte[] plaintext)
    {
        lock (Sync)
        {
            if (!Sessions.TryGetValue(hub, out var session))
            {
                plaintext = null;
                return false;
            }

            plaintext = AuthTransportCrypto.Decrypt(
                session.ClientToServerKey,
                session.SessionId,
                session.TranscriptHash,
                AuthTransportDirection.ClientToServer,
                envelope);
            return true;
        }
    }

    public static bool IsActive(ReferenceHub hub)
    {
        if (hub == null)
            return false;
        lock (Sync)
            return Sessions.ContainsKey(hub);
    }

    public static void Remove(ReferenceHub hub)
    {
        if (hub == null)
            return;
        lock (Sync)
        {
            if (Sessions.TryRemove(hub, out var session))
                RemoveSession(session);
            QuarantinedHubs.TryRemove(hub, out _);
            if (hub.connectionToClient != null) QuarantinedConnections.Remove(hub.connectionToClient);
        }
    }

    /// <summary>
    /// Revokes key material immediately while retaining the hub as unresolved
    /// until PlayerEvents.Left or teardown confirms disconnect initiation.
    /// </summary>
    public static void Quarantine(ReferenceHub hub)
    {
        if (hub == null)
            return;
        lock (Sync)
        {
            if (Sessions.TryRemove(hub, out var session))
                RemoveSession(session);
            QuarantinedHubs.TryAdd(hub, 0);
            if (hub.connectionToClient != null) QuarantinedConnections.Add(hub.connectionToClient);
        }
    }

    public static ReferenceHub[] SnapshotHubs()
    {
        lock (Sync)
        {
            var hubs = new HashSet<ReferenceHub>(Sessions.Keys);
            foreach (ReferenceHub hub in QuarantinedHubs.Keys)
                hubs.Add(hub);
            var result = new ReferenceHub[hubs.Count];
            hubs.CopyTo(result);
            return result;
        }
    }

    public static void Reset()
    {
        lock (Sync)
        {
            foreach (var connectionSession in new List<TransportSession>(Connections.Values))
            {
                QuarantinedConnections.Add(connectionSession.Connection);
                if (connectionSession.Hub != null) QuarantinedHubs.TryAdd(connectionSession.Hub, 0);
                RemoveSession(connectionSession);
            }
            foreach (var pair in Sessions)
            {
                if (Sessions.TryRemove(pair.Key, out var session))
                {
                    session.Dispose();
                    QuarantinedHubs.TryAdd(pair.Key, 0);
                }
            }
        }
    }

    private static void ClearPlayerMaterial(PlayerStorage player)
    {
        player.ClearSecrets();
    }

    private sealed class TransportSession : IDisposable
    {
        public TransportSession(PlayerStorage player)
        {
            if (player.ClientToServerKey?.Length != 32 || player.ServerToClientKey?.Length != 32 ||
                player.SessionId?.Length != AuthTransportCrypto.SessionIdSize ||
                player.TranscriptHash?.Length != AuthTransportCrypto.TranscriptHashSize)
                throw new ArgumentException("Invalid directional authenticated transport session material.", nameof(player));

            Player = player;
            ClientToServerKey = (byte[])player.ClientToServerKey.Clone();
            ServerToClientKey = (byte[])player.ServerToClientKey.Clone();
            SessionId = (byte[])player.SessionId.Clone();
            TranscriptHash = (byte[])player.TranscriptHash.Clone();
        }

        internal PlayerStorage Player { get; }
        internal ReferenceHub Hub { get; set; }
        internal NetworkConnectionToClient Connection { get; set; }
        internal AnomalyReplayState Replay { get; } = new AnomalyReplayState();
        internal AnomalyInboundRateLimiter RawLimiter { get; } = new AnomalyInboundRateLimiter();
        internal AnomalyInboundRateLimiter MessageLimiter { get; } = new AnomalyInboundRateLimiter();
        public byte[] ClientToServerKey { get; }
        public byte[] ServerToClientKey { get; }
        public byte[] SessionId { get; }
        public byte[] TranscriptHash { get; }

        public void Dispose()
        {
            EnrollmentSessionRegistry.Cancel(Player.UserId, Player.EnrollmentReservation);
            Player.AcceptedPeer = null;
            Player.ClearSecrets();
            Array.Clear(ClientToServerKey, 0, ClientToServerKey.Length);
            Array.Clear(ServerToClientKey, 0, ServerToClientKey.Length);
            Array.Clear(SessionId, 0, SessionId.Length);
            Array.Clear(TranscriptHash, 0, TranscriptHash.Length);
        }
    }
}
