using System;
using System.IO;
using System.Text;
using Anomaly.Shared.Networking;

namespace Anomaly.Shared.Auth;

public class ClientHandshake
{
    public const int IdentityPublicSize = 65;
    public const int SessionPublicSize = 32;
    public const int NonceSize = 12;
    public const int RetryCookieSize = 40;
    public const int MaximumConnectionContextBytes = 255;
    private const int FixedBindingHeaderSize = 117;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public byte protocolVersion = AnomalyProtocol.CurrentVersion;
    public byte[] identityPublic; // ECDSA [65]
    public byte[] sessionPublic; // X25519 [32]
    public byte[] nonce; // Random [12]
    public byte gameMajor;
    public byte gameMinor;
    public byte gameRevision;
    public bool gameBackwardCompatibility;
    public byte gameBackwardRevision;
    public string connectionContext;
    public ClientIdentityRotationChain identityRotationChain; // Optional old-key lineage to identityPublic
    public byte[] retryCookie; // Stateless server retry cookie [0 or 40]

    // [1:protocol][65:identity][32:ephemeral][12:nonce]
    // [3:game version][1:backcompat][1:backrevision]
    // [2:context length][context][2:rotation length][rotation]
    // [1:cookie length][0|40:cookie]
    public const int MinimumSize = FixedBindingHeaderSize + 1 + 2 + 1;
    public const int MaximumSize = FixedBindingHeaderSize
        + MaximumConnectionContextBytes
        + 2
        + ClientIdentityRotationChain.MaximumEncodedSize
        + 1
        + RetryCookieSize;

    public byte[] Encode()
    {
        ValidateFields();
        var cookieLength = retryCookie?.Length ?? 0;
        if (cookieLength != 0 && cookieLength != RetryCookieSize)
            throw new ArgumentException($"Retry cookie must be empty or {RetryCookieSize} bytes.", nameof(retryCookie));

        var binding = EncodeCookieBinding();
        var buf = new byte[binding.Length + 1 + cookieLength];
        Array.Copy(binding, buf, binding.Length);
        buf[binding.Length] = (byte)cookieLength;
        if (cookieLength != 0)
            Array.Copy(retryCookie, 0, buf, binding.Length + 1, cookieLength);
        return buf;
    }

    /// <summary>
    /// Returns the stable portion covered by the server's stateless retry cookie.
    /// The cookie itself is deliberately excluded.
    /// </summary>
    public byte[] EncodeCookieBinding()
    {
        ValidateFields();
        byte[] contextBytes = StrictUtf8.GetBytes(connectionContext);
        byte[] rotationBytes = identityRotationChain == null
            ? new byte[0]
            : identityRotationChain.EncodeStructural();
        var buf = new byte[FixedBindingHeaderSize + contextBytes.Length + 2 + rotationBytes.Length];
        buf[0] = protocolVersion;
        Array.Copy(identityPublic, 0, buf, 1, IdentityPublicSize);
        Array.Copy(sessionPublic, 0, buf, 66, SessionPublicSize);
        Array.Copy(nonce, 0, buf, 98, NonceSize);
        buf[110] = gameMajor;
        buf[111] = gameMinor;
        buf[112] = gameRevision;
        buf[113] = gameBackwardCompatibility ? (byte)1 : (byte)0;
        buf[114] = gameBackwardRevision;
        buf[115] = (byte)contextBytes.Length;
        buf[116] = (byte)(contextBytes.Length >> 8);
        Array.Copy(contextBytes, 0, buf, FixedBindingHeaderSize, contextBytes.Length);
        int rotationLengthOffset = FixedBindingHeaderSize + contextBytes.Length;
        buf[rotationLengthOffset] = (byte)rotationBytes.Length;
        buf[rotationLengthOffset + 1] = (byte)(rotationBytes.Length >> 8);
        if (rotationBytes.Length != 0)
            Array.Copy(rotationBytes, 0, buf, rotationLengthOffset + 2, rotationBytes.Length);
        return buf;
    }

    public static ClientHandshake Decode(byte[] buf)
    {
        if (buf == null)
            throw new ArgumentNullException(nameof(buf));
        if (buf.Length < 1)
            throw new ArgumentException(
                "ClientHandshake payload is empty.",
                nameof(buf));

        var version = buf[0];
        if (version != AnomalyProtocol.CurrentVersion)
            throw new AnomalyProtocolVersionMismatchException(version, AnomalyProtocol.CurrentVersion);

        if (buf.Length < MinimumSize || buf.Length > MaximumSize)
            throw new ArgumentException(
                $"ClientHandshake payload length {buf.Length} outside accepted range [{MinimumSize}, {MaximumSize}].",
                nameof(buf));

        int contextLength = buf[115] | (buf[116] << 8);
        if (contextLength == 0 || contextLength > MaximumConnectionContextBytes)
            throw new ArgumentException("ClientHandshake connection context length is invalid.", nameof(buf));
        int rotationLengthOffset = FixedBindingHeaderSize + contextLength;
        if (rotationLengthOffset + 2 > buf.Length)
            throw new ArgumentException("ClientHandshake rotation length was truncated.", nameof(buf));
        int rotationLength = buf[rotationLengthOffset] | (buf[rotationLengthOffset + 1] << 8);
        if (rotationLength > ClientIdentityRotationChain.MaximumEncodedSize)
            throw new ArgumentException("ClientHandshake rotation chain was too large.", nameof(buf));
        int cookieLengthOffset = rotationLengthOffset + 2 + rotationLength;
        if (cookieLengthOffset >= buf.Length)
            throw new ArgumentException("ClientHandshake rotation chain was truncated.", nameof(buf));

        var cookieLength = buf[cookieLengthOffset];
        if (cookieLength != 0 && cookieLength != RetryCookieSize)
            throw new ArgumentException($"ClientHandshake retry cookie length {cookieLength} is invalid.", nameof(buf));
        if (buf.Length != cookieLengthOffset + 1 + cookieLength)
            throw new ArgumentException("ClientHandshake payload length does not match its retry cookie length.", nameof(buf));

        var handshake = new ClientHandshake();
        handshake.protocolVersion = version;
        handshake.identityPublic = new byte[IdentityPublicSize];
        handshake.sessionPublic = new byte[SessionPublicSize];
        handshake.nonce = new byte[NonceSize];
        handshake.gameMajor = buf[110];
        handshake.gameMinor = buf[111];
        handshake.gameRevision = buf[112];
        if (buf[113] > 1)
            throw new ArgumentException("ClientHandshake backward-compatibility flag is invalid.", nameof(buf));
        handshake.gameBackwardCompatibility = buf[113] == 1;
        handshake.gameBackwardRevision = buf[114];
        byte[] contextBytes = new byte[contextLength];
        handshake.retryCookie = new byte[cookieLength];
        Array.Copy(buf, 1, handshake.identityPublic, 0, IdentityPublicSize);
        Array.Copy(buf, 66, handshake.sessionPublic, 0, SessionPublicSize);
        Array.Copy(buf, 98, handshake.nonce, 0, NonceSize);
        Array.Copy(buf, FixedBindingHeaderSize, contextBytes, 0, contextBytes.Length);
        handshake.connectionContext = StrictUtf8.GetString(contextBytes);
        if (rotationLength != 0)
        {
            var rotationBytes = new byte[rotationLength];
            Array.Copy(buf, rotationLengthOffset + 2, rotationBytes, 0, rotationBytes.Length);
            handshake.identityRotationChain = ClientIdentityRotationChain.Decode(rotationBytes);
        }
        if (cookieLength != 0)
            Array.Copy(buf, cookieLengthOffset + 1, handshake.retryCookie, 0, cookieLength);
        handshake.ValidateFields();
        return handshake;
    }

    public bool MatchesGameVersion(
        byte major,
        byte minor,
        byte revision,
        bool backwardCompatibility,
        byte backwardRevision)
    {
        return gameMajor == major &&
               gameMinor == minor &&
               gameRevision == revision &&
               gameBackwardCompatibility == backwardCompatibility &&
               gameBackwardRevision == (backwardCompatibility ? backwardRevision : (byte)0);
    }

    /// <summary>
    /// Returns the original public key for a valid rotation chain, or the current
    /// key when no rotation was presented. Server-local enrollment is derived from
    /// this value while the phase-two login still requires the current private key.
    /// </summary>
    public byte[] GetEnrollmentIdentityPublic()
    {
        if (!TryValidateIdentityRotationChain(out var enrollmentIdentity, out var error))
            throw new InvalidDataException("Client identity rotation chain was invalid: " + error);
        return enrollmentIdentity;
    }

    public bool TryValidateIdentityRotationChain(
        out byte[] enrollmentIdentityPublic,
        out string error)
    {
        enrollmentIdentityPublic = null;
        error = null;
        try
        {
            AuthCrypto.ReadPublic(identityPublic);
            if (identityRotationChain == null)
            {
                enrollmentIdentityPublic = (byte[])identityPublic.Clone();
                return true;
            }
            return identityRotationChain.TryValidate(
                identityPublic,
                out enrollmentIdentityPublic,
                out error);
        }
        catch (Exception ex)
        {
            enrollmentIdentityPublic = null;
            error = ex.Message;
            return false;
        }
    }

    private void ValidateFields()
    {
        if (protocolVersion != AnomalyProtocol.CurrentVersion)
            throw new AnomalyProtocolVersionMismatchException(protocolVersion, AnomalyProtocol.CurrentVersion);
        if (identityPublic == null || identityPublic.Length != IdentityPublicSize)
            throw new ArgumentException($"Identity public key must be {IdentityPublicSize} bytes.", nameof(identityPublic));
        if (sessionPublic == null || sessionPublic.Length != SessionPublicSize)
            throw new ArgumentException($"Session public key must be {SessionPublicSize} bytes.", nameof(sessionPublic));
        if (nonce == null || nonce.Length != NonceSize)
            throw new ArgumentException($"Client auth nonce must be {NonceSize} bytes.", nameof(nonce));
        if (gameMajor == 0)
            throw new ArgumentException("Game major version must be non-zero.", nameof(gameMajor));
        if (!gameBackwardCompatibility && gameBackwardRevision != 0)
            throw new ArgumentException("Backward revision must be zero when backward compatibility is disabled.", nameof(gameBackwardRevision));
        if (!AuthConnectionContext.IsNormalizedEndpoint(connectionContext))
            throw new ArgumentException("Connection context must be a normalized host:port endpoint.", nameof(connectionContext));
        int contextBytes = StrictUtf8.GetByteCount(connectionContext);
        if (contextBytes == 0 || contextBytes > MaximumConnectionContextBytes)
            throw new ArgumentException(
                $"Connection context must encode to 1-{MaximumConnectionContextBytes} UTF-8 bytes.",
                nameof(connectionContext));
        if (identityRotationChain != null
            && !identityRotationChain.TryValidateStructure(identityPublic, true, out var rotationError))
            throw new ArgumentException(
                "Client identity rotation chain was invalid: " + rotationError,
                nameof(identityRotationChain));
    }
}
