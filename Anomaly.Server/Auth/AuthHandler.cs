using System;
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
    internal static Dictionary<IPEndPoint, PlayerStorage> _storedPlayers = new();

    internal static string AddConnection(ServerAuthSession session,ClientHandshake handshake)
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
