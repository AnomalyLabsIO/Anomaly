using System;
using Anomaly.Shared.Networking;

namespace Anomaly.Shared.Auth;

public class ServerHandshake
{
    public byte protocolVersion = AnomalyProtocol.CurrentVersion;
    public byte[] sessionPublic; // X25519 [32]
    public byte[] challenge; // Random [20]

    // [1:version][32:sessionPublic][20:challenge]
    public const int Size = 53;

    public byte[] Encode()
    {
        var buf = new byte[Size];
        buf[0] = protocolVersion;
        Array.Copy(sessionPublic, 0, buf, 1, 32);
        Array.Copy(challenge, 0, buf, 33, 20);
        return buf;
    }

    public static ServerHandshake Decode(byte[] buf)
    {
        if (buf == null)
            throw new ArgumentNullException(nameof(buf));
        if (buf.Length < Size)
            throw new ArgumentException(
                $"ServerHandshake payload too short: expected >= {Size} bytes, got {buf.Length}.",
                nameof(buf));

        var version = buf[0];
        if (version != AnomalyProtocol.CurrentVersion)
            throw new AnomalyProtocolVersionMismatchException(version, AnomalyProtocol.CurrentVersion);

        var handshake = new ServerHandshake();
        handshake.protocolVersion = version;
        handshake.sessionPublic = new byte[32];
        handshake.challenge = new byte[20];
        Array.Copy(buf, 1, handshake.sessionPublic, 0, 32);
        Array.Copy(buf, 33, handshake.challenge, 0, 20);
        return handshake;
    }
}
