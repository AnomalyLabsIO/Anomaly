using System;
using System.IO;
using System.Text;

namespace Anomaly.Shared.Auth;

public class ClientLoginAttempt
{
    public byte[] signature; // ECDSA DER + AES-GCM tag. Typical ~96 bytes; bounds below give ample headroom.

    private static readonly byte[] ExtendedPayloadMagic = Encoding.ASCII.GetBytes("ANLP");
    private const byte ExtendedPayloadVersion = 2;
    private const int MaxSignatureBytes = 128;

    /// <summary>Lower bound: ECDSA DER signatures can't legitimately be shorter than this.</summary>
    public const int MinSize = 32;

    /// <summary>Upper bound: prevents a client claiming a gigantic signature from forcing per-connection allocations.</summary>
    public const int MaxSize = 1024;

    public byte[] Encode()
    {
        return signature;
    }

    public static ClientLoginAttempt Decode(byte[] buf)
    {
        if (buf == null)
            throw new ArgumentNullException(nameof(buf));
        if (buf.Length < MinSize || buf.Length > MaxSize)
            throw new ArgumentException(
                $"ClientLoginAttempt payload length {buf.Length} outside accepted range [{MinSize}, {MaxSize}].",
                nameof(buf));

        var attempt = new ClientLoginAttempt();
        attempt.signature = buf;
        return attempt;
    }

    internal static byte[] EncodePlainPayload(byte[] signature, byte[] passwordProof, byte[] encodedInventory)
    {
        if (signature == null)
            throw new ArgumentNullException(nameof(signature));
        if (encodedInventory == null)
            throw new ArgumentNullException(nameof(encodedInventory));

        if (signature.Length < MinSize || signature.Length > MaxSignatureBytes)
            throw new ArgumentException(
                $"ECDSA signature length {signature.Length} outside accepted range [{MinSize}, {MaxSignatureBytes}].",
                nameof(signature));
        if (passwordProof != null && passwordProof.Length != PasswordAuth.ProofSize)
            throw new ArgumentException($"Password proof must be {PasswordAuth.ProofSize} bytes.", nameof(passwordProof));
        // Decode also verifies canonical order and the aggregate encoded size.
        ClientModInventory.Decode(encodedInventory);

        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write(ExtendedPayloadMagic);
            writer.Write(ExtendedPayloadVersion);
            writer.Write((ushort)signature.Length);
            writer.Write(signature);
            writer.Write((byte)(passwordProof?.Length ?? 0));
            if (passwordProof != null)
                writer.Write(passwordProof);
            writer.Write((ushort)encodedInventory.Length);
            writer.Write(encodedInventory);
            writer.Flush();
            if (ms.Length + 16 > MaxSize) // AES-GCM tag
                throw new ArgumentException($"Encrypted login attempt cannot exceed {MaxSize} bytes.");
            return ms.ToArray();
        }
    }

    internal static bool TryDecodePlainPayload(
        byte[] payload,
        out byte[] signature,
        out byte[] passwordProof,
        out ClientModInventory inventory)
    {
        signature = null;
        passwordProof = null;
        inventory = null;

        if (payload == null || payload.Length < MinSize || payload.Length + 16 > MaxSize)
            return false;

        if (!HasExtendedPayloadMagic(payload))
            return false;

        try
        {
            using (var ms = new MemoryStream(payload))
            using (var reader = new BinaryReader(ms))
            {
                reader.ReadBytes(ExtendedPayloadMagic.Length);
                var version = reader.ReadByte();
                if (version != ExtendedPayloadVersion)
                    return false;

                var signatureLength = reader.ReadUInt16();
                if (signatureLength < MinSize || signatureLength > MaxSignatureBytes)
                    return false;

                signature = reader.ReadBytes(signatureLength);
                if (signature.Length != signatureLength)
                    return false;

                var proofLength = reader.ReadByte();
                if (proofLength != 0 && proofLength != PasswordAuth.ProofSize)
                    return false;

                var proof = reader.ReadBytes(proofLength);
                if (proof.Length != proofLength)
                    return false;
                passwordProof = proofLength == 0 ? null : proof;

                var inventoryLength = reader.ReadUInt16();
                if (inventoryLength < 1 || inventoryLength > ClientModInventory.MaxEncodedBytes)
                    return false;
                var inventoryBytes = reader.ReadBytes(inventoryLength);
                if (inventoryBytes.Length != inventoryLength)
                    return false;
                inventory = ClientModInventory.Decode(inventoryBytes);

                return ms.Position == ms.Length;
            }
        }
        catch
        {
            signature = null;
            passwordProof = null;
            inventory = null;
            return false;
        }
    }

    private static bool HasExtendedPayloadMagic(byte[] payload)
    {
        if (payload.Length < ExtendedPayloadMagic.Length)
            return false;

        for (var i = 0; i < ExtendedPayloadMagic.Length; i++)
        {
            if (payload[i] != ExtendedPayloadMagic[i])
                return false;
        }

        return true;
    }
}
