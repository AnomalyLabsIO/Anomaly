using System;

namespace Anomaly.Shared.Auth;

/// <summary>
/// Protocol-v2 key schedule output. LegacyTransportKey is null unless a caller
/// explicitly requests the loopback-only protocol-migration key schedule.
/// </summary>
public sealed class AuthSessionKeys : IDisposable
{
    internal AuthSessionKeys(
        byte[] clientToServerKey,
        byte[] serverToClientKey,
        byte[] loginKey,
        byte[] legacyTransportKey,
        byte[] sessionId,
        byte[] transcriptHash)
    {
        ClientToServerKey = clientToServerKey;
        ServerToClientKey = serverToClientKey;
        LoginKey = loginKey;
        LegacyTransportKey = legacyTransportKey;
        SessionId = sessionId;
        TranscriptHash = transcriptHash;
    }

    public byte[] ClientToServerKey { get; }
    public byte[] ServerToClientKey { get; }
    public byte[] LoginKey { get; }
    public byte[] LegacyTransportKey { get; }
    public byte[] SessionId { get; }
    public byte[] TranscriptHash { get; }

    public void Dispose()
    {
        Clear(ClientToServerKey);
        Clear(ServerToClientKey);
        Clear(LoginKey);
        Clear(LegacyTransportKey);
        Clear(SessionId);
        Clear(TranscriptHash);
    }

    private static void Clear(byte[] value)
    {
        if (value != null)
            Array.Clear(value, 0, value.Length);
    }
}
