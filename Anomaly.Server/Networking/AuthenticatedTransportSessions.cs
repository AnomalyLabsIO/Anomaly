using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Anomaly.Server.Auth;
using Anomaly.Shared.Auth;

namespace Anomaly.Server.Networking;

/// <summary>
/// Owns directional authenticated transport material after the preauth-to-Mirror
/// handoff. Key material is removed and zeroed when the hub leaves.
/// </summary>
internal static class AuthenticatedTransportSessions
{
    private static readonly object Sync = new object();
    private static readonly ConcurrentDictionary<ReferenceHub, TransportSession> Sessions =
        new ConcurrentDictionary<ReferenceHub, TransportSession>();
    private static readonly ConcurrentDictionary<ReferenceHub, byte> QuarantinedHubs =
        new ConcurrentDictionary<ReferenceHub, byte>();

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
                previous.Dispose();
            QuarantinedHubs.TryRemove(hub, out _);
            Sessions[hub] = replacement;
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
                session.Dispose();
            QuarantinedHubs.TryRemove(hub, out _);
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
                session.Dispose();
            QuarantinedHubs.TryAdd(hub, 0);
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

            ClientToServerKey = (byte[])player.ClientToServerKey.Clone();
            ServerToClientKey = (byte[])player.ServerToClientKey.Clone();
            SessionId = (byte[])player.SessionId.Clone();
            TranscriptHash = (byte[])player.TranscriptHash.Clone();
        }

        public byte[] ClientToServerKey { get; }
        public byte[] ServerToClientKey { get; }
        public byte[] SessionId { get; }
        public byte[] TranscriptHash { get; }

        public void Dispose()
        {
            Array.Clear(ClientToServerKey, 0, ClientToServerKey.Length);
            Array.Clear(ServerToClientKey, 0, ServerToClientKey.Length);
            Array.Clear(SessionId, 0, SessionId.Length);
            Array.Clear(TranscriptHash, 0, TranscriptHash.Length);
        }
    }
}
