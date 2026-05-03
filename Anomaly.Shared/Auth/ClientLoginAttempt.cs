using System;
using System.IO;
using System.Text;

namespace Anomaly.Shared.Auth;

public class ClientLoginAttempt
{
    public byte[] signature; // ECDSA DER + AES-GCM tag. Typical ~96 bytes; bounds below give ample headroom.

    private static readonly byte[] ExtendedPayloadMagic = Encoding.ASCII.GetBytes("ANLP");
    private const byte ExtendedPayloadVersion = 1;

    /// <summary>Lower bound: ECDSA DER signatures can't legitimately be shorter than this.</summary>
    public const int MinSize = 32;

    /// <summary>Upper bound: prevents a client claiming a gigantic signature from forcing per-connection allocations.</summary>
    public const int MaxSize = 512;

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

    internal static byte[] EncodePlainPayload(byte[] signature, byte[] passwordProof)
    {
        if (signature == null)
            throw new ArgumentNullException(nameof(signature));

        if (passwordProof == null)
            return signature;

        if (signature.Length < MinSize || signature.Length > MaxSize)
            throw new ArgumentException(
                $"ECDSA signature length {signature.Length} outside accepted range [{MinSize}, {MaxSize}].",
                nameof(signature));
        if (passwordProof.Length != PasswordAuth.ProofSize)
            throw new ArgumentException($"Password proof must be {PasswordAuth.ProofSize} bytes.", nameof(passwordProof));

        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write(ExtendedPayloadMagic);
            writer.Write(ExtendedPayloadVersion);
            writer.Write((ushort)signature.Length);
            writer.Write(signature);
            writer.Write((byte)passwordProof.Length);
            writer.Write(passwordProof);
            writer.Flush();
            return ms.ToArray();
        }
    }

    internal static bool TryDecodePlainPayload(byte[] payload, out byte[] signature, out byte[] passwordProof)
    {
        signature = null;
        passwordProof = null;

        if (payload == null || payload.Length < MinSize)
            return false;

        if (!HasExtendedPayloadMagic(payload))
        {
            signature = payload;
            return true;
        }

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
                if (signatureLength < MinSize || signatureLength > MaxSize)
                    return false;

                signature = reader.ReadBytes(signatureLength);
                if (signature.Length != signatureLength)
                    return false;

                var proofLength = reader.ReadByte();
                if (proofLength != PasswordAuth.ProofSize)
                    return false;

                passwordProof = reader.ReadBytes(proofLength);
                if (passwordProof.Length != proofLength)
                    return false;

                return ms.Position == ms.Length;
            }
        }
        catch
        {
            signature = null;
            passwordProof = null;
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
