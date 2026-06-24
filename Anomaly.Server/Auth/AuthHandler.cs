using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using Anomaly.Shared.Auth;

namespace Anomaly.Server.Auth;

public static class AuthHandler
{
    // Phase 1 handshake entries are ephemeral — a client that abandons Phase 2 would
    // otherwise leak one entry per abandoned attempt. 60s covers the reconnect window
    // used by the client (~2s) with wide margin for slow networks.
    private static readonly TimeSpan HandshakeTtl = TimeSpan.FromSeconds(60);
    private static readonly object _connectionsLock = new();

    internal static Dictionary<string, Connection> _connections = new();

    // CONCURRENCY: _storedPlayers is written from the off-main-thread preauth path
    // (LiteNetLib4MirrorServer.ConnectionRequestLoop -> ProcessConnectionRequest, which
    // runs on the dedicated "ConnectionRequestThread" in the live build) and read/removed
    // from the main thread (PlayerAuthenticationManager.FixedUpdate). It MUST be a
    // ConcurrentDictionary — a plain Dictionary mutated across threads can corrupt its
    // bucket chain and freeze a reader in an infinite FindEntry loop. Access only through
    // StorePlayer / TryTakePlayer so TTL eviction stays consistent.
    internal static readonly ConcurrentDictionary<IPEndPoint, PlayerStorage> _storedPlayers = new();
    private static readonly TimeSpan StoredPlayerTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Records a preauthenticated player keyed by endpoint, stamping it for TTL eviction.
    /// Called off the main thread from the preauth path.
    /// </summary>
    internal static void StorePlayer(IPEndPoint endPoint, PlayerStorage player)
    {
        player.CreatedAtUtc = DateTime.UtcNow;
        EvictExpiredStoredPlayers();
        _storedPlayers[endPoint] = player;
    }

    /// <summary>
    /// Atomically removes and returns the stored player for <paramref name="endPoint"/>.
    /// Returns false if absent or past its TTL (a client that preauthed but never connected).
    /// </summary>
    internal static bool TryTakePlayer(IPEndPoint endPoint, out PlayerStorage player)
    {
        if (!_storedPlayers.TryRemove(endPoint, out player))
            return false;

        if (DateTime.UtcNow - player.CreatedAtUtc > StoredPlayerTtl)
        {
            player = null;
            return false;
        }

        return true;
    }

    private static void EvictExpiredStoredPlayers()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _storedPlayers) // ConcurrentDictionary enumeration is safe under concurrent mutation
        {
            if (now - kvp.Value.CreatedAtUtc > StoredPlayerTtl)
                _storedPlayers.TryRemove(kvp.Key, out _);
        }
    }

    internal static string AddConnection(ServerAuthSession session, ClientHandshake handshake, PasswordChallenge passwordChallenge = null)
    {
        string random;
        lock (_connectionsLock)
        {
            EvictExpiredLocked();

            do
            {
                random = RandomGenerator.GetStringSecure(50);
            }
            while(_connections.ContainsKey(random));

            _connections[random] = new Connection
            {
                ServerEnv = session,
                Handshake = handshake,
                CreatedAtUtc = DateTime.UtcNow,
                PasswordRequired = passwordChallenge?.Required == true,
                PasswordSalt = passwordChallenge?.Salt,
                PasswordIterations = passwordChallenge?.Iterations ?? 0,
                PasswordNonce = passwordChallenge?.Nonce,
            };
        }

        return random;
    }

    // Returns true only when the identifier exists AND is still within TTL.
    // Expired entries are removed as a side effect.
    internal static bool TryConsumeConnection(string identifier, out Connection connection)
    {
        lock (_connectionsLock)
        {
            if (!_connections.TryGetValue(identifier, out connection))
                return false;

            if (DateTime.UtcNow - connection.CreatedAtUtc > HandshakeTtl)
            {
                _connections.Remove(identifier);
                connection = null;
                return false;
            }

            return true;
        }
    }

    internal static void RemoveConnection(string identifier)
    {
        lock (_connectionsLock)
        {
            _connections.Remove(identifier);
        }
    }

    private static void EvictExpiredLocked()
    {
        var now = DateTime.UtcNow;
        List<string> expired = null;
        foreach (var kvp in _connections)
        {
            if (now - kvp.Value.CreatedAtUtc > HandshakeTtl)
            {
                expired ??= new List<string>();
                expired.Add(kvp.Key);
            }
        }
        if (expired == null) return;
        foreach (var key in expired)
            _connections.Remove(key);
    }
}
