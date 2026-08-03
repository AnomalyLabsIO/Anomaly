using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Anomaly.Shared.Auth;

/// <summary>
/// A bounded, contiguous delegation chain from the enrollment's original public
/// key to the current client key. The server can therefore preserve a local
/// enrollment without keeping its own global-key alias database.
/// </summary>
public sealed class ClientIdentityRotationChain
{
    private const byte SchemaVersion = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ANOMALY-CLIENT-KEY-CHAIN-V1");

    public const int MaximumLinks = 4;
    public const int MaximumEncodedSize = 1280;

    public List<ClientIdentityRotationRecord> Records { get; set; } =
        new List<ClientIdentityRotationRecord>();

    public static ClientIdentityRotationChain Create(byte[] oldIdentity, byte[] newIdentity)
    {
        var chain = new ClientIdentityRotationChain();
        chain.Records.Add(ClientIdentityRotationRecord.Create(oldIdentity, newIdentity, 1));
        chain.ValidateInternal(null, false, out _, out _);
        return chain;
    }

    public ClientIdentityRotationChain Append(byte[] oldIdentity, byte[] newIdentity)
    {
        byte[] oldPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(oldIdentity));
        if (!TryValidate(oldPublic, out _, out var error))
            throw new InvalidOperationException("Stored client rotation chain does not end at the old identity: " + error);
        if (Records.Count >= MaximumLinks)
            throw new InvalidOperationException(
                $"Client identity rotation chain reached its {MaximumLinks}-link public-v1 ceiling.");

        var next = Clone();
        next.Records.Add(ClientIdentityRotationRecord.Create(
            oldIdentity,
            newIdentity,
            checked((ulong)Records.Count + 1)));
        byte[] newPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(newIdentity));
        if (!next.TryValidate(newPublic, out _, out error))
            throw new InvalidOperationException("New client rotation chain was invalid: " + error);
        return next;
    }

    public bool TryValidate(
        byte[] advertisedCurrentIdentityPublic,
        out byte[] enrollmentIdentityPublic,
        out string error)
        => ValidateInternal(advertisedCurrentIdentityPublic, true, out enrollmentIdentityPublic, out error);

    public byte[] Encode()
    {
        if (!ValidateInternal(null, false, out _, out var error))
            throw new InvalidDataException(error);

        return EncodeStructural();
    }

    internal byte[] EncodeStructural()
    {
        if (!TryValidateStructure(null, false, out var error))
            throw new InvalidDataException(error);

        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(Magic);
            writer.Write(SchemaVersion);
            writer.Write((byte)Records.Count);
            foreach (var record in Records)
            {
                byte[] encodedRecord = record.EncodeStructural();
                writer.Write((ushort)encodedRecord.Length);
                writer.Write(encodedRecord);
            }
            writer.Flush();
            byte[] encoded = stream.ToArray();
            if (encoded.Length > MaximumEncodedSize)
                throw new InvalidDataException("Client rotation chain exceeded its wire ceiling.");
            return encoded;
        }
    }

    public static ClientIdentityRotationChain Decode(byte[] encoded)
    {
        if (encoded == null)
            throw new ArgumentNullException(nameof(encoded));
        if (encoded.Length == 0 || encoded.Length > MaximumEncodedSize)
            throw new InvalidDataException("Client rotation chain length was invalid.");

        using (var stream = new MemoryStream(encoded, false))
        using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
        {
            byte[] magic = ReadExact(reader, Magic.Length, "domain marker");
            if (!PasswordAuth.FixedTimeEquals(magic, Magic))
                throw new InvalidDataException("Client rotation chain domain marker was invalid.");
            if (reader.ReadByte() != SchemaVersion)
                throw new InvalidDataException("Client rotation chain schema was unsupported.");
            int count = reader.ReadByte();
            if (count == 0 || count > MaximumLinks)
                throw new InvalidDataException("Client rotation chain link count was invalid.");

            var chain = new ClientIdentityRotationChain();
            for (var i = 0; i < count; i++)
            {
                int recordLength = reader.ReadUInt16();
                if (recordLength == 0 || recordLength > ClientIdentityRotationRecord.MaximumEncodedSize)
                    throw new InvalidDataException("Client rotation chain record length was invalid.");
                chain.Records.Add(ClientIdentityRotationRecord.Decode(
                    ReadExact(reader, recordLength, "record")));
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Client rotation chain contained trailing bytes.");
            if (!chain.TryValidateStructure(null, false, out var error))
                throw new InvalidDataException(error);
            return chain;
        }
    }

    public ClientIdentityRotationChain Clone() => Decode(Encode());

    internal bool TryValidateStructure(
        byte[] advertisedCurrentIdentityPublic,
        bool requireAdvertisedCurrent,
        out string error)
    {
        error = null;
        if (Records == null || Records.Count == 0 || Records.Count > MaximumLinks)
        {
            error = $"Client rotation chain must contain 1-{MaximumLinks} records.";
            return false;
        }

        byte[] expectedOld = null;
        for (var i = 0; i < Records.Count; i++)
        {
            var record = Records[i];
            if (record?.OldIdentityPublic == null
                || record.OldIdentityPublic.Length != ClientHandshake.IdentityPublicSize
                || record.NewIdentityPublic == null
                || record.NewIdentityPublic.Length != ClientHandshake.IdentityPublicSize
                || record.Signature == null
                || record.Signature.Length < 64
                || record.Signature.Length > 80)
            {
                error = "Client rotation chain contained an invalid fixed-size field.";
                return false;
            }
            if (record.Sequence != checked((ulong)i + 1))
            {
                error = "Client rotation chain sequence was non-contiguous, replayed, or reordered.";
                return false;
            }
            if (i != 0 && !PasswordAuth.FixedTimeEquals(expectedOld, record.OldIdentityPublic))
            {
                error = "Client rotation chain links were not contiguous.";
                return false;
            }
            expectedOld = record.NewIdentityPublic;
        }

        if (requireAdvertisedCurrent
            && (advertisedCurrentIdentityPublic == null
                || advertisedCurrentIdentityPublic.Length != ClientHandshake.IdentityPublicSize
                || !PasswordAuth.FixedTimeEquals(expectedOld, advertisedCurrentIdentityPublic)))
        {
            error = "Client rotation chain did not terminate at the advertised current identity.";
            return false;
        }
        return true;
    }

    private bool ValidateInternal(
        byte[] advertisedCurrentIdentityPublic,
        bool requireAdvertisedCurrent,
        out byte[] enrollmentIdentityPublic,
        out string error)
    {
        enrollmentIdentityPublic = null;
        error = null;
        try
        {
            if (!TryValidateStructure(
                    advertisedCurrentIdentityPublic,
                    requireAdvertisedCurrent,
                    out error))
            {
                return false;
            }

            byte[] expectedOld = Records[0]?.OldIdentityPublic;
            AuthCrypto.ReadPublic(expectedOld);
            enrollmentIdentityPublic = (byte[])expectedOld.Clone();
            for (var i = 0; i < Records.Count; i++)
            {
                var record = Records[i];
                if (record == null || !record.Validate(
                        expectedOld,
                        record.NewIdentityPublic,
                        checked((ulong)i + 1),
                        out error))
                {
                    enrollmentIdentityPublic = null;
                    return false;
                }
                expectedOld = record.NewIdentityPublic;
            }

            if (requireAdvertisedCurrent
                && !PasswordAuth.FixedTimeEquals(expectedOld, advertisedCurrentIdentityPublic))
            {
                enrollmentIdentityPublic = null;
                error = "Client rotation chain did not terminate at the advertised current identity.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            enrollmentIdentityPublic = null;
            error = "Client rotation chain was malformed: " + ex.Message;
            return false;
        }
    }

    private static byte[] ReadExact(BinaryReader reader, int count, string field)
    {
        byte[] value = reader.ReadBytes(count);
        if (value.Length != count)
            throw new EndOfStreamException("Client rotation chain " + field + " was truncated.");
        return value;
    }
}
