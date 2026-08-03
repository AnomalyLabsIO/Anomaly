using System;
using System.Collections.Concurrent;
using Anomaly.Server.Auth;
using Anomaly.Shared.Auth;

namespace Anomaly.Server.Networking;

/// <summary>
/// Owns protocol-v2 directional transport material after the preauth-to-Mirror
/// handoff. Key material is removed and zeroed when the hub leaves.
/// </summary>
internal static class AuthenticatedTransportSessions
{
    private static readonly ConcurrentDictionary<ReferenceHub, TransportSession> Sessions =
        new ConcurrentDictionary<ReferenceHub, TransportSession>();

    public static void Register(ReferenceHub hub, PlayerStorage player)
    {
        if (hub == null)
            throw new ArgumentNullException(nameof(hub));
        if (player == null)
            throw new ArgumentNullException(nameof(player));

        var replacement = new TransportSession(player);
        if (Sessions.TryRemove(hub, out var previous))
            previous.Dispose();
        Sessions[hub] = replacement;
        ClearPlayerMaterial(player);
    }

    public static bool TryEncryptToClient(ReferenceHub hub, byte[] plaintext, out byte[] envelope)
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

    public static bool TryDecryptFromClient(ReferenceHub hub, byte[] envelope, out byte[] plaintext)
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

    public static void Remove(ReferenceHub hub)
    {
        if (hub != null && Sessions.TryRemove(hub, out var session))
            session.Dispose();
    }

    public static void Reset()
    {
        foreach (var pair in Sessions)
        {
            if (Sessions.TryRemove(pair.Key, out var session))
                session.Dispose();
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
                throw new ArgumentException("Invalid protocol-v2 transport session material.", nameof(player));

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
