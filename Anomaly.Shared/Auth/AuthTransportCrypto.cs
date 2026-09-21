using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Anomaly.Shared.Auth;

/// <summary>
/// Directional AEAD envelope format v2 for authenticated encrypted-channel traffic.
/// The marker is intentionally outside the ciphertext so receivers can reject
/// legacy bidirectional envelopes without first trying a legacy key.
/// </summary>
public static class AuthTransportCrypto
{
    public const int SessionIdSize = 16;
    public const int TranscriptHashSize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int MarkerSize = 4;
    public const int EnvelopeOverhead = MarkerSize + NonceSize + TagSize;

    private static readonly byte[] Marker = { 0x41, 0x4E, 0x32, 0x45 }; // AN2E
    private static readonly byte[] AadDomain = Encoding.ASCII.GetBytes("ANOMALY-ENCRYPTED-CHANNEL-V2");

    public static byte[] Encrypt(
        byte[] key,
        byte[] sessionId,
        byte[] transcriptHash,
        AuthTransportDirection direction,
        byte[] plaintext)
    {
        ValidateContext(key, sessionId, transcriptHash, direction);
        if (plaintext == null)
            throw new ArgumentNullException(nameof(plaintext));

        var nonce = new byte[NonceSize];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(nonce);

        var aad = BuildAssociatedData(sessionId, transcriptHash, direction);
        var ciphertext = AuthCrypto.AesEncrypt(key, nonce, plaintext, aad);
        var envelope = new byte[MarkerSize + nonce.Length + ciphertext.Length];
        Array.Copy(Marker, 0, envelope, 0, MarkerSize);
        Array.Copy(nonce, 0, envelope, MarkerSize, nonce.Length);
        Array.Copy(ciphertext, 0, envelope, MarkerSize + nonce.Length, ciphertext.Length);
        Array.Clear(aad, 0, aad.Length);
        return envelope;
    }

    public static byte[] Decrypt(
        byte[] key,
        byte[] sessionId,
        byte[] transcriptHash,
        AuthTransportDirection direction,
        byte[] envelope)
    {
        ValidateContext(key, sessionId, transcriptHash, direction);
        if (!IsVersion2Envelope(envelope) || envelope.Length < EnvelopeOverhead)
            throw new CryptographicException("Directional AEAD v2 encrypted-channel envelope is malformed.");

        var nonce = new byte[NonceSize];
        var ciphertext = new byte[envelope.Length - MarkerSize - NonceSize];
        Array.Copy(envelope, MarkerSize, nonce, 0, nonce.Length);
        Array.Copy(envelope, MarkerSize + NonceSize, ciphertext, 0, ciphertext.Length);

        var aad = BuildAssociatedData(sessionId, transcriptHash, direction);
        try
        {
            return AuthCrypto.AesDecrypt(key, nonce, ciphertext, aad);
        }
        finally
        {
            Array.Clear(aad, 0, aad.Length);
            Array.Clear(nonce, 0, nonce.Length);
            Array.Clear(ciphertext, 0, ciphertext.Length);
        }
    }

    public static bool IsVersion2Envelope(byte[] envelope)
    {
        if (envelope == null || envelope.Length < MarkerSize)
            return false;

        var difference = 0;
        for (var i = 0; i < MarkerSize; i++)
            difference |= envelope[i] ^ Marker[i];
        return difference == 0;
    }

    internal static byte[] BuildAssociatedData(
        byte[] sessionId,
        byte[] transcriptHash,
        AuthTransportDirection direction)
    {
        if (sessionId == null || sessionId.Length != SessionIdSize)
            throw new ArgumentException($"Session ID must be {SessionIdSize} bytes.", nameof(sessionId));
        if (transcriptHash == null || transcriptHash.Length != TranscriptHashSize)
            throw new ArgumentException($"Transcript hash must be {TranscriptHashSize} bytes.", nameof(transcriptHash));
        if (direction != AuthTransportDirection.ClientToServer &&
            direction != AuthTransportDirection.ServerToClient)
            throw new ArgumentOutOfRangeException(nameof(direction));

        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(AadDomain);
            writer.Write((byte)2);
            writer.Write((byte)direction);
            writer.Write(sessionId);
            writer.Write(transcriptHash);
            writer.Flush();
            return stream.ToArray();
        }
    }

    private static void ValidateContext(
        byte[] key,
        byte[] sessionId,
        byte[] transcriptHash,
        AuthTransportDirection direction)
    {
        if (key == null || key.Length != 32)
            throw new ArgumentException("Transport key must be 32 bytes.", nameof(key));
        if (sessionId == null || sessionId.Length != SessionIdSize)
            throw new ArgumentException($"Session ID must be {SessionIdSize} bytes.", nameof(sessionId));
        if (transcriptHash == null || transcriptHash.Length != TranscriptHashSize)
            throw new ArgumentException($"Transcript hash must be {TranscriptHashSize} bytes.", nameof(transcriptHash));
        if (direction != AuthTransportDirection.ClientToServer &&
            direction != AuthTransportDirection.ServerToClient)
            throw new ArgumentOutOfRangeException(nameof(direction));
    }
}

public enum AuthTransportDirection : byte
{
    ClientToServer = 1,
    ServerToClient = 2,
}
