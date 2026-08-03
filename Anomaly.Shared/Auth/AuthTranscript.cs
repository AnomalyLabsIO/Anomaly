using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Anomaly.Shared.Auth;

/// <summary>
/// Canonical protocol-v2 handshake transcript. Both peers sign or authenticate
/// exactly these bytes; changing any key, nonce, policy field, or session identifier
/// therefore invalidates the login attempt.
/// </summary>
public static class AuthTranscript
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ANOMALY-AUTH-V2");
    private static readonly byte[] LoginMagic = Encoding.ASCII.GetBytes("ANOMALY-LOGIN-V2");
    public const int MaximumServerIdentifierBytes = 128;
    public const int MaximumDisplayNameBytes = 128;

    public static byte[] Build(
        ClientHandshake client,
        ServerHandshake server,
        string serverIdentifier,
        PasswordChallenge passwordChallenge)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));
        if (server == null)
            throw new ArgumentNullException(nameof(server));

        // These methods validate all fixed-size fields without including the retry
        // cookie or server signature in the transcript.
        var clientBinding = client.EncodeCookieBinding();
        server.ValidateUnsignedFields();

        var identifierBytes = Encoding.UTF8.GetBytes(serverIdentifier ?? string.Empty);
        if (identifierBytes.Length == 0 || identifierBytes.Length > MaximumServerIdentifierBytes)
            throw new ArgumentException(
                $"Server identifier must encode to 1-{MaximumServerIdentifierBytes} UTF-8 bytes.",
                nameof(serverIdentifier));

        passwordChallenge ??= PasswordChallenge.NotRequired();
        ValidatePasswordChallenge(passwordChallenge);

        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(Magic);
            writer.Write((byte)2); // transcript schema version
            writer.Write(clientBinding);
            writer.Write(server.protocolVersion);
            writer.Write(server.sessionPublic);
            writer.Write(server.challenge);
            writer.Write(server.identityPublic);
            byte[] rotationRecord = server.GetRotationRecordBytes();
            writer.Write((ushort)rotationRecord.Length);
            if (rotationRecord.Length != 0)
                writer.Write(rotationRecord);
            writer.Write((ushort)identifierBytes.Length);
            writer.Write(identifierBytes);
            writer.Write(passwordChallenge.Required);
            if (passwordChallenge.Required)
            {
                writer.Write(passwordChallenge.Salt);
                writer.Write(passwordChallenge.Iterations);
                writer.Write(passwordChallenge.Nonce);
            }
            writer.Flush();
            return stream.ToArray();
        }
    }

    public static byte[] Hash(
        ClientHandshake client,
        ServerHandshake server,
        string serverIdentifier,
        PasswordChallenge passwordChallenge)
    {
        var transcript = Build(client, server, serverIdentifier, passwordChallenge);
        using (var sha = SHA256.Create())
            return sha.ComputeHash(transcript);
    }

    public static byte[] BuildLogin(
        ClientHandshake client,
        ServerHandshake server,
        string serverIdentifier,
        PasswordChallenge passwordChallenge,
        string displayName)
    {
        var transcript = Build(client, server, serverIdentifier, passwordChallenge);
        var displayNameBytes = Encoding.UTF8.GetBytes(displayName ?? string.Empty);
        if (displayNameBytes.Length == 0 || displayNameBytes.Length > MaximumDisplayNameBytes)
            throw new ArgumentException(
                $"Display name must encode to 1-{MaximumDisplayNameBytes} UTF-8 bytes.",
                nameof(displayName));

        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(transcript);
            writer.Write(LoginMagic);
            writer.Write((ushort)displayNameBytes.Length);
            writer.Write(displayNameBytes);
            writer.Flush();
            return stream.ToArray();
        }
    }

    public static byte[] HashLogin(
        ClientHandshake client,
        ServerHandshake server,
        string serverIdentifier,
        PasswordChallenge passwordChallenge,
        string displayName)
    {
        var transcript = BuildLogin(client, server, serverIdentifier, passwordChallenge, displayName);
        using (var sha = SHA256.Create())
            return sha.ComputeHash(transcript);
    }

    private static void ValidatePasswordChallenge(PasswordChallenge challenge)
    {
        if (!challenge.Required)
            return;
        if (challenge.Salt == null || challenge.Salt.Length != PasswordAuth.SaltSize)
            throw new ArgumentException($"Password salt must be {PasswordAuth.SaltSize} bytes.", nameof(challenge));
        if (challenge.Iterations < PasswordAuth.MinimumIterations
            || challenge.Iterations > PasswordAuth.MaximumIterations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(challenge),
                $"Password KDF iterations must be in [{PasswordAuth.MinimumIterations}, {PasswordAuth.MaximumIterations}].");
        }
        if (challenge.Nonce == null || challenge.Nonce.Length != PasswordAuth.NonceSize)
            throw new ArgumentException($"Password nonce must be {PasswordAuth.NonceSize} bytes.", nameof(challenge));
    }
}
