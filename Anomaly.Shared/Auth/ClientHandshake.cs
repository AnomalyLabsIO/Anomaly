using System;
using System.Security.Cryptography;
using Anomaly.Shared.Networking;

namespace Anomaly.Shared.Auth;

public class ClientHandshake
{
    public byte protocolVersion = AnomalyProtocol.CurrentVersion;
    public byte[] identityPublic; // ECDSA [65]
    public byte[] sessionPublic; // X25519 [32]
    public byte[] nonce; // Random [12]

    // [1:version][65:identityPublic][32:sessionPublic][12:nonce]
    public const int Size = 110;

    public byte[] Encode()
    {
        var buf = new byte[Size];
        buf[0] = protocolVersion;
        Array.Copy(identityPublic, 0, buf, 1, 65);
        Array.Copy(sessionPublic, 0, buf, 66, 32);
        Array.Copy(nonce, 0, buf, 98, 12);
        return buf;
    }

    public static ClientHandshake Decode(byte[] buf)
    {
        if (buf == null)
            throw new ArgumentNullException(nameof(buf));
        if (buf.Length < Size)
            throw new ArgumentException(
                $"ClientHandshake payload too short: expected >= {Size} bytes, got {buf.Length}.",
                nameof(buf));

        var version = buf[0];
        if (version != AnomalyProtocol.CurrentVersion)
            throw new AnomalyProtocolVersionMismatchException(version, AnomalyProtocol.CurrentVersion);

        var handshake = new ClientHandshake();
        handshake.protocolVersion = version;
        handshake.identityPublic = new byte[65];
        handshake.sessionPublic = new byte[32];
        handshake.nonce = new byte[12];
        Array.Copy(buf, 1, handshake.identityPublic, 0, 65);
        Array.Copy(buf, 66, handshake.sessionPublic, 0, 32);
        Array.Copy(buf, 98, handshake.nonce, 0, 12);
        return handshake;
    }

    public string GetUserId()
    {
        var sha = SHA256.Create();
        var hash = sha.ComputeHash(identityPublic);
        return Base58.Encode(hash)+"@anomaly";
    }
}
