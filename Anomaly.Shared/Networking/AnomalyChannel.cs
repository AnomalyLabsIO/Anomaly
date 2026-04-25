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
    }

    /// <summary>
    /// Anomaly wire/auth protocol version. Bumped on incompatible format changes
    /// so mismatched clients and servers reject each other at handshake time
    /// instead of silently mis-parsing payloads.
    /// </summary>
    public static class AnomalyProtocol
    {
        /// <summary>
        /// v1: added a 4-byte per-channel replay counter to every 0xFE envelope
        /// (between the channel byte and the message-hash). Prior to v1, the
        /// wire was [0xFE][hash][payload] and had no replay protection.
        /// </summary>
        public const byte CurrentVersion = 1;
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
