using System;
using System.Security.Cryptography;
using Anomaly.Shared.Networking;

namespace Anomaly.Shared.Auth;

public class ServerHandshake
{
    public const int SessionPublicSize = 32;
    public const int ChallengeSize = 20;
    public const int IdentityPublicSize = 65;
    public const int MinimumSignatureSize = 64;
    public const int MaximumSignatureSize = 80;

    public byte protocolVersion = AnomalyProtocol.CurrentVersion;
    public byte[] sessionPublic; // X25519 [32]
    public byte[] challenge; // Random [20]
    public byte[] identityPublic; // Persistent server ECDSA public key [65]
    public byte[] transcriptSignature; // ECDSA DER signature
    public ServerIdentityRotationRecord identityRotationRecord; // Optional old-key authorization of identityPublic

    // [1:version][32:sessionPublic][20:challenge][65:identityPublic]
    // [1:signatureLength][signature][2:rotationLength][rotation]
    public const int HeaderSize = 119;
    public const int MinimumSize = HeaderSize + MinimumSignatureSize + 2;
    public const int MaximumSize = HeaderSize + MaximumSignatureSize + 2 + ServerIdentityRotationRecord.MaximumEncodedSize;

    public byte[] Encode()
    {
        ValidateUnsignedFields();
        if (transcriptSignature == null
            || transcriptSignature.Length < MinimumSignatureSize
            || transcriptSignature.Length > MaximumSignatureSize)
        {
            throw new ArgumentException(
                $"Server transcript signature length must be in [{MinimumSignatureSize}, {MaximumSignatureSize}].",
                nameof(transcriptSignature));
        }

        byte[] rotationBytes = GetRotationRecordBytes();
        var buf = new byte[HeaderSize + transcriptSignature.Length + 2 + rotationBytes.Length];
        buf[0] = protocolVersion;
        Array.Copy(sessionPublic, 0, buf, 1, SessionPublicSize);
        Array.Copy(challenge, 0, buf, 33, ChallengeSize);
        Array.Copy(identityPublic, 0, buf, 53, IdentityPublicSize);
        buf[118] = (byte)transcriptSignature.Length;
        Array.Copy(transcriptSignature, 0, buf, HeaderSize, transcriptSignature.Length);
        int rotationOffset = HeaderSize + transcriptSignature.Length;
        buf[rotationOffset] = (byte)rotationBytes.Length;
        buf[rotationOffset + 1] = (byte)(rotationBytes.Length >> 8);
        if (rotationBytes.Length != 0)
            Array.Copy(rotationBytes, 0, buf, rotationOffset + 2, rotationBytes.Length);
        return buf;
    }

    public static ServerHandshake Decode(byte[] buf)
    {
        if (buf == null)
            throw new ArgumentNullException(nameof(buf));
        if (buf.Length < 1)
            throw new ArgumentException("ServerHandshake payload is empty.", nameof(buf));

        var version = buf[0];
        if (version != AnomalyProtocol.CurrentVersion)
            throw new AnomalyProtocolVersionMismatchException(version, AnomalyProtocol.CurrentVersion);

        if (buf.Length < MinimumSize || buf.Length > MaximumSize)
            throw new ArgumentException(
                $"ServerHandshake payload length {buf.Length} outside accepted range [{MinimumSize}, {MaximumSize}].",
                nameof(buf));

        var signatureLength = buf[118];
        if (signatureLength < MinimumSignatureSize || signatureLength > MaximumSignatureSize)
            throw new ArgumentException($"ServerHandshake signature length {signatureLength} is invalid.", nameof(buf));
        int rotationOffset = HeaderSize + signatureLength;
        if (buf.Length < rotationOffset + 2)
            throw new ArgumentException("ServerHandshake rotation length was truncated.", nameof(buf));
        int rotationLength = buf[rotationOffset] | (buf[rotationOffset + 1] << 8);
        if (rotationLength > ServerIdentityRotationRecord.MaximumEncodedSize ||
            buf.Length != rotationOffset + 2 + rotationLength)
            throw new ArgumentException("ServerHandshake rotation record length was invalid.", nameof(buf));

        var handshake = new ServerHandshake
        {
            protocolVersion = version,
            sessionPublic = new byte[SessionPublicSize],
            challenge = new byte[ChallengeSize],
            identityPublic = new byte[IdentityPublicSize],
            transcriptSignature = new byte[signatureLength],
        };
        Array.Copy(buf, 1, handshake.sessionPublic, 0, SessionPublicSize);
        Array.Copy(buf, 33, handshake.challenge, 0, ChallengeSize);
        Array.Copy(buf, 53, handshake.identityPublic, 0, IdentityPublicSize);
        Array.Copy(buf, HeaderSize, handshake.transcriptSignature, 0, signatureLength);
        if (rotationLength != 0)
        {
            var rotationBytes = new byte[rotationLength];
            Array.Copy(buf, rotationOffset + 2, rotationBytes, 0, rotationBytes.Length);
            handshake.identityRotationRecord = ServerIdentityRotationRecord.Decode(rotationBytes);
        }
        handshake.ValidateUnsignedFields();
        return handshake;
    }

    public string GetIdentityFingerprint()
    {
        ValidateUnsignedFields();
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(identityPublic);
            var chars = new char[hash.Length * 2];
            const string hex = "0123456789ABCDEF";
            for (var i = 0; i < hash.Length; i++)
            {
                chars[i * 2] = hex[hash[i] >> 4];
                chars[i * 2 + 1] = hex[hash[i] & 0x0F];
            }
            return new string(chars);
        }
    }

    internal void ValidateUnsignedFields()
    {
        if (sessionPublic == null || sessionPublic.Length != SessionPublicSize)
            throw new ArgumentException($"Server session public key must be {SessionPublicSize} bytes.", nameof(sessionPublic));
        if (challenge == null || challenge.Length != ChallengeSize)
            throw new ArgumentException($"Server challenge must be {ChallengeSize} bytes.", nameof(challenge));
        if (identityPublic == null || identityPublic.Length != IdentityPublicSize)
            throw new ArgumentException($"Server identity public key must be {IdentityPublicSize} bytes.", nameof(identityPublic));
        if (identityRotationRecord != null &&
            !PasswordAuth.FixedTimeEquals(identityRotationRecord.NewIdentityPublic, identityPublic))
            throw new ArgumentException("Server rotation record did not authorize the advertised identity.", nameof(identityRotationRecord));
    }

    internal byte[] GetRotationRecordBytes()
        => identityRotationRecord == null ? new byte[0] : identityRotationRecord.Encode();
}
