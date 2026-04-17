using System.Security.Cryptography;

namespace Anomaly.Installer;

/// <summary>
/// SHA-256 helpers used to verify that bytes written to disk match what we intended.
/// Protects against partial writes, AV interference during extraction, and silent disk corruption.
/// Note: does not protect against tampering of the Installer binary itself — use a signed release for that.
/// </summary>
internal static class ResourceIntegrity
{
    public static string ComputeSha256(Stream stream)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    public static string ComputeSha256(byte[] data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        return Convert.ToHexString(hash);
    }

    public static string ComputeFileSha256(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        return ComputeSha256(fs);
    }

    /// <summary>
    /// Throws <see cref="InstallerException"/> when <paramref name="actualHash"/> differs
    /// from <paramref name="expectedHash"/> (case-insensitive hex compare).
    /// </summary>
    public static void VerifyHash(string subject, string expectedHash, string actualHash)
    {
        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException(
                $"Integrity check failed for {subject}. Expected SHA-256 {expectedHash}, got {actualHash}.");
    }
}
