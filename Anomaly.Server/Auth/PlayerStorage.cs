using System;

namespace Anomaly.Server.Auth;

public class PlayerStorage
{
    public string NickName { get; set; }
    public string UserId { get; set; }
    public byte[] SharedKey { get; set; }

    /// <summary>
    /// Set by <see cref="AuthHandler.StorePlayer"/> when the preauth thread records this
    /// entry. Used to evict entries from clients that preauthenticate but never complete
    /// the Mirror connect (otherwise they leak forever, keyed by ip:port).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
}