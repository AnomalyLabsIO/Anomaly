using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Anomaly.Shared.Auth;

/// <summary>
/// A permanent, old-key-signed delegation from one client identity key to its
/// successor. Possession of the successor private key is still required by the
/// normal login signature; this public record only preserves enrollment lineage.
/// </summary>
public sealed class ClientIdentityRotationRecord
{
    private const byte SchemaVersion = 1;
    private const int MinimumSignatureSize = 64;
    private const int MaximumSignatureSize = 80;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ANOMALY-CLIENT-KEY-ROTATION-V1");

    public const int MaximumEncodedSize = 288;
    private static long _signatureVerificationCount;

    internal static long SignatureVerificationCount => Interlocked.Read(ref _signatureVerificationCount);

    internal static void ResetSignatureVerificationCount()
        => Interlocked.Exchange(ref _signatureVerificationCount, 0);

    public byte[] OldIdentityPublic { get; set; }
    public byte[] NewIdentityPublic { get; set; }
    public ulong Sequence { get; set; }
    public byte[] Signature { get; set; }

    public static ClientIdentityRotationRecord Create(
        byte[] oldIdentity,
        byte[] newIdentity,
        ulong sequence)
    {
        var oldPair = AuthCrypto.ReadIdentity(oldIdentity);
        var newPair = AuthCrypto.ReadIdentity(newIdentity);
        var record = new ClientIdentityRotationRecord
        {
            OldIdentityPublic = AuthCrypto.WritePublic(oldPair),
            NewIdentityPublic = AuthCrypto.WritePublic(newPair),
            Sequence = sequence,
        };
        record.Signature = AuthCrypto.SignData(record.BuildSignedPayload(), oldPair);
        record.ValidateStructure();
        return record;
    }

    public bool Validate(
        byte[] expectedOldIdentityPublic,
        byte[] expectedNewIdentityPublic,
        ulong expectedSequence,
        out string error)
    {
        error = null;
        try
        {
            ValidateCryptographicFields();
            if (!PasswordAuth.FixedTimeEquals(OldIdentityPublic, expectedOldIdentityPublic))
            {
                error = "Client rotation old key did not match the expected lineage.";
                return false;
            }
            if (!PasswordAuth.FixedTimeEquals(NewIdentityPublic, expectedNewIdentityPublic))
            {
                error = "Client rotation new key did not match the expected successor.";
                return false;
            }
            if (Sequence != expectedSequence)
            {
                error = "Client rotation sequence was non-contiguous, replayed, or reordered.";
                return false;
            }
            Interlocked.Increment(ref _signatureVerificationCount);
            if (!AuthCrypto.VerifyData(
                    BuildSignedPayload(),
                    Signature,
                    AuthCrypto.ReadPublic(OldIdentityPublic)))
            {
                error = "Client rotation signature was invalid.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = "Client rotation record was malformed: " + ex.Message;
            return false;
        }
    }

    public byte[] Encode()
    {
        if (!Validate(OldIdentityPublic, NewIdentityPublic, Sequence, out var error))
            throw new InvalidDataException(error);
        return EncodeStructural();
    }

    internal byte[] EncodeStructural()
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
                throw new InvalidDataException("Client rotation record exceeded its wire ceiling.");
            return encoded;
        }
    }

    public static ClientIdentityRotationRecord Decode(byte[] encoded)
    {
        if (encoded == null)
            throw new ArgumentNullException(nameof(encoded));
        if (encoded.Length == 0 || encoded.Length > MaximumEncodedSize)
            throw new InvalidDataException("Client rotation record length was invalid.");

        using (var stream = new MemoryStream(encoded, false))
        using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
        {
            byte[] magic = ReadExact(reader, Magic.Length, "domain marker");
            if (!PasswordAuth.FixedTimeEquals(magic, Magic))
                throw new InvalidDataException("Client rotation record domain marker was invalid.");
            if (reader.ReadByte() != SchemaVersion)
                throw new InvalidDataException("Client rotation record schema was unsupported.");

            var record = new ClientIdentityRotationRecord
            {
                OldIdentityPublic = ReadExact(reader, ClientHandshake.IdentityPublicSize, "old identity"),
                NewIdentityPublic = ReadExact(reader, ClientHandshake.IdentityPublicSize, "new identity"),
                Sequence = reader.ReadUInt64(),
            };
            int signatureLength = reader.ReadByte();
            if (signatureLength < MinimumSignatureSize || signatureLength > MaximumSignatureSize)
                throw new InvalidDataException("Client rotation signature length was invalid.");
            record.Signature = ReadExact(reader, signatureLength, "signature");
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Client rotation record contained trailing bytes.");
            record.ValidateStructure();
            return record;
        }
    }

    public ClientIdentityRotationRecord Clone() => Decode(Encode());

    private byte[] BuildSignedPayload()
    {
        ValidateStructure(requireSignature: false);
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(Magic);
            writer.Write(SchemaVersion);
            writer.Write(OldIdentityPublic);
            writer.Write(NewIdentityPublic);
            writer.Write(Sequence);
            writer.Flush();
            return stream.ToArray();
        }
    }

    private void ValidateStructure()
        => ValidateStructure(requireSignature: true);

    private void ValidateStructure(bool requireSignature)
    {
        if (OldIdentityPublic == null || OldIdentityPublic.Length != ClientHandshake.IdentityPublicSize)
            throw new InvalidDataException("Client rotation old public key length was invalid.");
        if (NewIdentityPublic == null || NewIdentityPublic.Length != ClientHandshake.IdentityPublicSize)
            throw new InvalidDataException("Client rotation new public key length was invalid.");
        if (PasswordAuth.FixedTimeEquals(OldIdentityPublic, NewIdentityPublic))
            throw new InvalidDataException("Client rotation must change the identity key.");
        if (Sequence == 0)
            throw new InvalidDataException("Client rotation sequence must be non-zero.");
        if (requireSignature
            && (Signature == null || Signature.Length < MinimumSignatureSize || Signature.Length > MaximumSignatureSize))
            throw new InvalidDataException("Client rotation signature length was invalid.");
    }

    private void ValidateCryptographicFields()
    {
        ValidateStructure();
        AuthCrypto.ReadPublic(OldIdentityPublic);
        AuthCrypto.ReadPublic(NewIdentityPublic);
    }

    private static byte[] ReadExact(BinaryReader reader, int count, string field)
    {
        byte[] value = reader.ReadBytes(count);
        if (value.Length != count)
            throw new EndOfStreamException("Client rotation " + field + " was truncated.");
        return value;
    }
}
