using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Anomaly.Shared.Auth;

/// <summary>
/// Canonical proof that a server identity key authorized its successor. The
/// proof is independent of a connection handshake so it can be persisted and
/// replayed to clients that reconnect during its bounded validity window.
/// </summary>
public sealed class ServerIdentityRotationRecord
{
    private const byte SchemaVersion = 1;
    private const int FingerprintSize = 32;
    private const int MinimumSignatureSize = 64;
    private const int MaximumSignatureSize = 80;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ANOMALY-SERVER-KEY-ROTATION-V1");

    public const int MaximumEncodedSize = 512;
    public const long MaximumValiditySeconds = 366L * 24L * 60L * 60L;

    public byte[] OldIdentityPublic { get; set; }
    public byte[] OldFingerprint { get; set; }
    public byte[] NewIdentityPublic { get; set; }
    public byte[] NewFingerprint { get; set; }
    public long NotBeforeUnixSeconds { get; set; }
    public long ExpiresUnixSeconds { get; set; }
    public ulong Sequence { get; set; }
    public byte[] Signature { get; set; }

    public static ServerIdentityRotationRecord Create(
        byte[] oldIdentity,
        byte[] newIdentity,
        long notBeforeUnixSeconds,
        long expiresUnixSeconds,
        ulong sequence)
    {
        var oldPair = AuthCrypto.ReadIdentity(oldIdentity);
        var newPair = AuthCrypto.ReadIdentity(newIdentity);
        var record = new ServerIdentityRotationRecord
        {
            OldIdentityPublic = AuthCrypto.WritePublic(oldPair),
            NewIdentityPublic = AuthCrypto.WritePublic(newPair),
            NotBeforeUnixSeconds = notBeforeUnixSeconds,
            ExpiresUnixSeconds = expiresUnixSeconds,
            Sequence = sequence,
        };
        record.OldFingerprint = ComputeFingerprint(record.OldIdentityPublic);
        record.NewFingerprint = ComputeFingerprint(record.NewIdentityPublic);
        byte[] payload = record.BuildSignedPayload();
        record.Signature = AuthCrypto.SignData(payload, oldPair);
        record.ValidateStructure();
        return record;
    }

    public bool Validate(
        byte[] pinnedOldIdentityPublic,
        byte[] advertisedNewIdentityPublic,
        long nowUnixSeconds,
        ulong lastAcceptedSequence,
        out string error)
    {
        error = null;
        try
        {
            ValidateStructure();
            if (!FixedTimeEquals(OldIdentityPublic, pinnedOldIdentityPublic))
            {
                error = "Rotation proof old key did not match the pinned server key.";
                return false;
            }
            if (!FixedTimeEquals(NewIdentityPublic, advertisedNewIdentityPublic))
            {
                error = "Rotation proof new key did not match the advertised server key.";
                return false;
            }
            if (nowUnixSeconds < NotBeforeUnixSeconds)
            {
                error = "Rotation proof is not active yet.";
                return false;
            }
            if (nowUnixSeconds > ExpiresUnixSeconds)
            {
                error = "Rotation proof has expired.";
                return false;
            }
            if (Sequence <= lastAcceptedSequence)
            {
                error = "Rotation proof sequence was replayed or rolled back.";
                return false;
            }

            byte[] payload = BuildSignedPayload();
            if (!AuthCrypto.VerifyData(payload, Signature, AuthCrypto.ReadPublic(OldIdentityPublic)))
            {
                error = "Rotation proof signature was invalid.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = "Rotation proof was malformed: " + ex.Message;
            return false;
        }
    }

    public byte[] Encode()
    {
        ValidateStructure();
        byte[] payload = BuildSignedPayload();
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(payload);
            writer.Write((byte)Signature.Length);
            writer.Write(Signature);
            writer.Flush();
            byte[] encoded = stream.ToArray();
            if (encoded.Length > MaximumEncodedSize)
                throw new InvalidDataException("Rotation proof exceeded its wire ceiling.");
            return encoded;
        }
    }

    public static ServerIdentityRotationRecord Decode(byte[] encoded)
    {
        if (encoded == null)
            throw new ArgumentNullException(nameof(encoded));
        if (encoded.Length == 0 || encoded.Length > MaximumEncodedSize)
            throw new InvalidDataException("Rotation proof length was invalid.");

        using (var stream = new MemoryStream(encoded, false))
        using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
        {
            byte[] magic = reader.ReadBytes(Magic.Length);
            if (!FixedTimeEquals(magic, Magic))
                throw new InvalidDataException("Rotation proof domain marker was invalid.");
            if (reader.ReadByte() != SchemaVersion)
                throw new InvalidDataException("Rotation proof schema version was unsupported.");

            var record = new ServerIdentityRotationRecord
            {
                OldIdentityPublic = ReadExact(reader, ClientHandshake.IdentityPublicSize, "old identity"),
                OldFingerprint = ReadExact(reader, FingerprintSize, "old fingerprint"),
                NewIdentityPublic = ReadExact(reader, ClientHandshake.IdentityPublicSize, "new identity"),
                NewFingerprint = ReadExact(reader, FingerprintSize, "new fingerprint"),
                NotBeforeUnixSeconds = reader.ReadInt64(),
                ExpiresUnixSeconds = reader.ReadInt64(),
                Sequence = reader.ReadUInt64(),
            };
            int signatureLength = reader.ReadByte();
            if (signatureLength < MinimumSignatureSize || signatureLength > MaximumSignatureSize)
                throw new InvalidDataException("Rotation proof signature length was invalid.");
            record.Signature = ReadExact(reader, signatureLength, "signature");
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Rotation proof contained trailing bytes.");
            record.ValidateStructure();
            return record;
        }
    }

    public ServerIdentityRotationRecord Clone() => Decode(Encode());

    public static byte[] ComputeFingerprint(byte[] identityPublic)
    {
        if (identityPublic == null || identityPublic.Length != ClientHandshake.IdentityPublicSize)
            throw new ArgumentException(
                $"Identity public key must be {ClientHandshake.IdentityPublicSize} bytes.",
                nameof(identityPublic));
        AuthCrypto.ReadPublic(identityPublic);
        using (var sha = SHA256.Create())
            return sha.ComputeHash(identityPublic);
    }

    public static string FingerprintHex(byte[] identityPublic)
        => BitConverter.ToString(ComputeFingerprint(identityPublic)).Replace("-", string.Empty);

    private byte[] BuildSignedPayload()
    {
        ValidateUnsignedFields();
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(Magic);
            writer.Write(SchemaVersion);
            writer.Write(OldIdentityPublic);
            writer.Write(OldFingerprint);
            writer.Write(NewIdentityPublic);
            writer.Write(NewFingerprint);
            writer.Write(NotBeforeUnixSeconds);
            writer.Write(ExpiresUnixSeconds);
            writer.Write(Sequence);
            writer.Flush();
            return stream.ToArray();
        }
    }

    private void ValidateStructure()
    {
        ValidateUnsignedFields();
        if (Signature == null || Signature.Length < MinimumSignatureSize || Signature.Length > MaximumSignatureSize)
            throw new InvalidDataException("Rotation proof signature length was invalid.");
    }

    private void ValidateUnsignedFields()
    {
        AuthCrypto.ReadPublic(OldIdentityPublic);
        AuthCrypto.ReadPublic(NewIdentityPublic);
        if (!FixedTimeEquals(OldFingerprint, ComputeFingerprint(OldIdentityPublic)))
            throw new InvalidDataException("Rotation proof old fingerprint did not match its public key.");
        if (!FixedTimeEquals(NewFingerprint, ComputeFingerprint(NewIdentityPublic)))
            throw new InvalidDataException("Rotation proof new fingerprint did not match its public key.");
        if (NotBeforeUnixSeconds < 0 || ExpiresUnixSeconds <= NotBeforeUnixSeconds)
            throw new InvalidDataException("Rotation proof validity interval was invalid.");
        if (ExpiresUnixSeconds - NotBeforeUnixSeconds > MaximumValiditySeconds)
            throw new InvalidDataException("Rotation proof validity interval exceeded the protocol ceiling.");
        if (Sequence == 0)
            throw new InvalidDataException("Rotation proof sequence must be non-zero.");
    }

    private static byte[] ReadExact(BinaryReader reader, int count, string field)
    {
        byte[] value = reader.ReadBytes(count);
        if (value.Length != count)
            throw new EndOfStreamException("Rotation proof " + field + " was truncated.");
        return value;
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
        => PasswordAuth.FixedTimeEquals(left, right);
}
