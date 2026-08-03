using System;

namespace Anomaly.Server.Auth;

public class PlayerStorage
{
    public string NickName { get; set; }
    public string UserId { get; set; }
    public byte[] ClientToServerKey { get; set; }
    public byte[] ServerToClientKey { get; set; }
    public byte[] SessionId { get; set; }
    public byte[] TranscriptHash { get; set; }

    /// <summary>
    /// Set by <see cref="AuthHandler.StorePlayer"/> when the preauth thread records this
    /// entry. Used to evict entries from clients that preauthenticate but never complete
    /// the Mirror connect (otherwise they leak forever, keyed by ip:port).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    internal void ClearSecrets()
    {
        Clear(ClientToServerKey);
        Clear(ServerToClientKey);
        Clear(SessionId);
        Clear(TranscriptHash);
        ClientToServerKey = null;
        ServerToClientKey = null;
        SessionId = null;
        TranscriptHash = null;
    }

    private static void Clear(byte[] value)
    {
        if (value != null)
            Array.Clear(value, 0, value.Length);
    }
}
