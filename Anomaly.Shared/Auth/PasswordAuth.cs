using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Anomaly.Shared.Auth;

public static class PasswordAuth
{
    public const int SaltSize = 16;
    public const int NonceSize = 16;
    public const int VerifierSize = 32;
    public const int ProofSize = 32;
    public const int MinimumIterations = 10000;
    public const int DefaultIterations = 120000;

    public static byte[] GenerateSalt() => GetRandomBytes(SaltSize);

    public static byte[] GenerateNonce() => GetRandomBytes(NonceSize);

    public static byte[] DeriveVerifier(string password, byte[] salt, int iterations)
    {
        if (password == null)
            throw new ArgumentNullException(nameof(password));
        if (salt == null || salt.Length != SaltSize)
            throw new ArgumentException($"Password salt must be {SaltSize} bytes.", nameof(salt));
        if (iterations < MinimumIterations)
            throw new ArgumentOutOfRangeException(nameof(iterations), $"Password KDF iterations must be at least {MinimumIterations}.");

        using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            return kdf.GetBytes(VerifierSize);
    }

    public static byte[] CreateProof(
        byte[] verifier,
        byte[] clientNonce,
        byte[] serverChallenge,
        string serverIdentifier,
        byte[] passwordNonce)
    {
        if (verifier == null || verifier.Length != VerifierSize)
            throw new ArgumentException($"Password verifier must be {VerifierSize} bytes.", nameof(verifier));
        if (clientNonce == null || clientNonce.Length != 12)
            throw new ArgumentException("Client auth nonce must be 12 bytes.", nameof(clientNonce));
        if (serverChallenge == null || serverChallenge.Length != 20)
            throw new ArgumentException("Server auth challenge must be 20 bytes.", nameof(serverChallenge));
        if (passwordNonce == null || passwordNonce.Length != NonceSize)
            throw new ArgumentException($"Password nonce must be {NonceSize} bytes.", nameof(passwordNonce));

        using (var hmac = new HMACSHA256(verifier))
            return hmac.ComputeHash(BuildProofInput(clientNonce, serverChallenge, serverIdentifier, passwordNonce));
    }

    public static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
            return false;

        var diff = 0;
        for (var i = 0; i < left.Length; i++)
            diff |= left[i] ^ right[i];

        return diff == 0;
    }

    private static byte[] BuildProofInput(
        byte[] clientNonce,
        byte[] serverChallenge,
        string serverIdentifier,
        byte[] passwordNonce)
    {
        var serverIdentifierBytes = Encoding.UTF8.GetBytes(serverIdentifier ?? string.Empty);
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write(clientNonce);
            writer.Write(serverChallenge);
            writer.Write(passwordNonce);
            writer.Write(serverIdentifierBytes.Length);
            writer.Write(serverIdentifierBytes);
            writer.Flush();
            return ms.ToArray();
        }
    }

    private static byte[] GetRandomBytes(int size)
    {
        var bytes = new byte[size];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(bytes);
        return bytes;
    }
}
