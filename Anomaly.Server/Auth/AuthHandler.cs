using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using Anomaly.Shared.Auth;

namespace Anomaly.Server.Auth;

public static class AuthHandler
{
    private static readonly TimeSpan HandshakeTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan StoredPlayerTtl = TimeSpan.FromSeconds(60);
    private const int MaximumPendingConnections = 2048;
    private const int MaximumPendingConnectionsPerAddress = 8;
    private const int MaximumExpiryRemovalsPerOperation = 64;
    private const int MaximumStoredPlayers = 2048;

    private static readonly object ConnectionsLock = new object();
    private static readonly Dictionary<string, Connection> Connections = new Dictionary<string, Connection>();
    private static readonly Dictionary<string, int> PendingByAddress = new Dictionary<string, int>();
    private static readonly LinkedList<string> ExpiryOrder = new LinkedList<string>();

    // Written from LiteNetLib's connection-request thread and consumed from
    // PlayerAuthenticationManager.FixedUpdate on Unity's main thread.
    internal static readonly ConcurrentDictionary<IPEndPoint, PlayerStorage> _storedPlayers = new ConcurrentDictionary<IPEndPoint, PlayerStorage>();

    internal static int PendingConnectionCount
    {
        get
        {
            lock (ConnectionsLock)
                return Connections.Count;
        }
    }

    internal static int StoredPlayerCount => _storedPlayers.Count;

    internal static bool TryReserveConnection(
        ClientHandshake handshake,
        IPAddress sourceAddress,
        PasswordChallenge passwordChallenge,
        out string identifier,
        out Connection connection)
    {
        identifier = null;
        connection = null;
        if (handshake == null || sourceAddress == null)
            return false;

        var addressKey = NormalizeAddress(sourceAddress);
        lock (ConnectionsLock)
        {
            EvictExpiredLocked(DateTime.UtcNow, MaximumExpiryRemovalsPerOperation);
            if (Connections.Count >= MaximumPendingConnections)
                return false;
            if (PendingByAddress.TryGetValue(addressKey, out var perAddress)
                && perAddress >= MaximumPendingConnectionsPerAddress)
                return false;

            do
            {
                identifier = RandomGenerator.GetStringSecure(50);
            }
            while (Connections.ContainsKey(identifier));

            var node = ExpiryOrder.AddLast(identifier);
            connection = new Connection
            {
                Handshake = handshake,
                PasswordChallenge = passwordChallenge ?? PasswordChallenge.NotRequired(),
                SourceAddress = new IPAddress(PreauthGate.NormalizeAddressBytes(sourceAddress)),
                CreatedAtUtc = DateTime.UtcNow,
                PasswordRequired = passwordChallenge?.Required == true,
                PasswordSalt = passwordChallenge?.Salt,
                PasswordIterations = passwordChallenge?.Iterations ?? 0,
                PasswordNonce = passwordChallenge?.Nonce,
                ExpiryNode = node,
            };
            Connections.Add(identifier, connection);
            PendingByAddress[addressKey] = perAddress + 1;
            return true;
        }
    }

    /// <summary>
    /// Atomically removes phase-one state before validation. The source IP is bound;
    /// the UDP port is deliberately not, because the expected reconnect between phases
    /// normally obtains a different ephemeral port.
    /// </summary>
    internal static bool TryConsumeConnection(string identifier, IPAddress sourceAddress, out Connection connection)
    {
        connection = null;
        if (string.IsNullOrEmpty(identifier) || sourceAddress == null)
            return false;

        lock (ConnectionsLock)
        {
            EvictExpiredLocked(DateTime.UtcNow, MaximumExpiryRemovalsPerOperation);
            if (!Connections.TryGetValue(identifier, out var found))
                return false;

            if (DateTime.UtcNow - found.CreatedAtUtc > HandshakeTtl)
            {
                RemoveLocked(identifier, found);
                return false;
            }

            // A packet from the wrong endpoint must not consume the legitimate
            // client's one-time phase-two state. Keep the check and removal in
            // the same lock so only the bound endpoint can win the consume race.
            if (!PreauthGate.AddressesEqual(found.SourceAddress, sourceAddress))
                return false;

            RemoveLocked(identifier, found);
            connection = found;
            return true;
        }
    }

    internal static void RemoveConnection(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return;
        lock (ConnectionsLock)
        {
            if (Connections.TryGetValue(identifier, out var connection))
                RemoveLocked(identifier, connection);
        }
    }

    internal static bool StorePlayer(IPEndPoint endPoint, PlayerStorage player)
    {
        if (endPoint == null || player == null)
            return false;
        player.CreatedAtUtc = DateTime.UtcNow;
        EvictExpiredStoredPlayers(MaximumExpiryRemovalsPerOperation);
        if (_storedPlayers.Count >= MaximumStoredPlayers && !_storedPlayers.ContainsKey(endPoint))
        {
            EnrollmentSessionRegistry.Cancel(player.UserId, player.EnrollmentReservation);
            player.ClearSecrets();
            return false;
        }
        _storedPlayers.AddOrUpdate(
            endPoint,
            player,
            (_, previous) =>
            {
                EnrollmentSessionRegistry.Cancel(previous.UserId, previous.EnrollmentReservation);
                previous.ClearSecrets();
                return player;
            });
        return true;
    }

    internal static bool TryTakePlayer(IPEndPoint endPoint, out PlayerStorage player)
    {
        if (!_storedPlayers.TryRemove(endPoint, out player))
            return false;
        if (DateTime.UtcNow - player.CreatedAtUtc > StoredPlayerTtl)
        {
            EnrollmentSessionRegistry.Cancel(player.UserId, player.EnrollmentReservation);
            player.ClearSecrets();
            player = null;
            return false;
        }
        return true;
    }

    internal static void RemoveStoredPlayer(IPEndPoint endPoint)
    {
        if (endPoint == null || !_storedPlayers.TryRemove(endPoint, out var player))
            return;
        EnrollmentSessionRegistry.Cancel(player.UserId, player.EnrollmentReservation);
        player.ClearSecrets();
    }

    /// <summary>
    /// Drains accepted phase-two handoffs that have not yet been claimed by
    /// PlayerAuthenticationManager.FixedUpdate. Callers use the returned count
    /// to decide whether Harmony admission/authentication patches must remain
    /// installed until those already-accepted peers are rejected.
    /// </summary>
    internal static int DrainStoredPlayers()
    {
        int drained = 0;
        foreach (var pair in _storedPlayers)
        {
            if (!_storedPlayers.TryRemove(pair.Key, out var player))
                continue;
            EnrollmentSessionRegistry.Cancel(player.UserId, player.EnrollmentReservation);
            player.ClearSecrets();
            drained++;
        }
        return drained;
    }

    internal static void Reset()
    {
        lock (ConnectionsLock)
        {
            Connections.Clear();
            PendingByAddress.Clear();
            ExpiryOrder.Clear();
        }
        DrainStoredPlayers();
    }

    private static void EvictExpiredLocked(DateTime now, int maximumRemovals)
    {
        var removed = 0;
        while (ExpiryOrder.First != null && removed < maximumRemovals)
        {
            var identifier = ExpiryOrder.First.Value;
            if (!Connections.TryGetValue(identifier, out var connection))
            {
                ExpiryOrder.RemoveFirst();
                removed++;
                continue;
            }
            if (now - connection.CreatedAtUtc <= HandshakeTtl)
                break;
            RemoveLocked(identifier, connection);
            removed++;
        }
    }

    private static void RemoveLocked(string identifier, Connection connection)
    {
        Connections.Remove(identifier);
        if (connection.ExpiryNode?.List != null)
            ExpiryOrder.Remove(connection.ExpiryNode);

        var addressKey = NormalizeAddress(connection.SourceAddress);
        if (PendingByAddress.TryGetValue(addressKey, out var count))
        {
            if (count <= 1)
                PendingByAddress.Remove(addressKey);
            else
                PendingByAddress[addressKey] = count - 1;
        }
    }

    private static void EvictExpiredStoredPlayers(int maximumRemovals)
    {
        var now = DateTime.UtcNow;
        var removed = 0;
        foreach (var pair in _storedPlayers)
        {
            if (removed >= maximumRemovals)
                break;
            if (now - pair.Value.CreatedAtUtc <= StoredPlayerTtl)
                continue;
            if (_storedPlayers.TryRemove(pair.Key, out var expired))
            {
                EnrollmentSessionRegistry.Cancel(expired.UserId, expired.EnrollmentReservation);
                expired.ClearSecrets();
                removed++;
            }
        }
    }

    private static string NormalizeAddress(IPAddress address)
        => address == null ? string.Empty : new IPAddress(PreauthGate.NormalizeAddressBytes(address)).ToString();
}
