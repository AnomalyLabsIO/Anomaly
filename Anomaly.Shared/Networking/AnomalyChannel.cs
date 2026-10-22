using System;

namespace Anomaly.Shared.Networking
{
    public static class AnomalyChannel
    {
        /// <summary>
        /// Reserved EncryptedChannel byte value for Anomaly custom messages.
        /// This value must not collide with any game EncryptedChannel enum value.
        /// </summary>
        public const byte ChannelId = 0xFE;

        /// <summary>
        /// Absolute decoded envelope ceiling. Individual message policies are
        /// normally much smaller; this protects the hash lookup and allocation
        /// boundary for legacy messages that do not yet declare a ceiling.
        /// </summary>
        public const int MaxEnvelopeBytes = 2 * 1024 * 1024;

        /// <summary>
        /// Directional AEAD envelopes add a 4-byte marker, 12-byte nonce and
        /// 16-byte authentication tag. Keep a conservative framing margin.
        /// </summary>
        public const int MaxEncryptedEnvelopeBytes = MaxEnvelopeBytes + 64;
    }

    /// <summary>
    /// Anomaly wire/auth protocol version. Bumped on incompatible format changes
    /// so mismatched clients and servers reject each other at handshake time
    /// instead of silently mis-parsing payloads.
    /// </summary>
    public static class AnomalyProtocol
    {
        /// <summary>
        /// v8: binds lifetime mod inventory and mandatory integrity audit in the encrypted signed login. Older versions are intentionally
        /// rejected; there is no silent downgrade path.
        /// </summary>
        public const byte CurrentVersion = 8;
    }

    /// <summary>
    /// Thrown by handshake decoders when the peer advertises a protocol version
    /// we don't speak. The handshake path catches this and rejects the
    /// connection with a clear human-readable reason.
    /// </summary>
    public class AnomalyProtocolVersionMismatchException : Exception
    {
        public byte PeerVersion { get; }
        public byte OurVersion { get; }

        public AnomalyProtocolVersionMismatchException(byte peerVersion, byte ourVersion)
            : base($"Anomaly protocol version mismatch: peer={peerVersion}, ours={ourVersion}. Update client and server to matching versions.")
        {
            PeerVersion = peerVersion;
            OurVersion = ourVersion;
        }
    }
}
