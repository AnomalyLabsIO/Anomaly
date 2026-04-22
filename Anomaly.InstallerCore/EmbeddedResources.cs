using System.Reflection;
using System.Security.Cryptography;

namespace Anomaly.InstallerCore;

public static class EmbeddedResources
{
    private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();

    /// <summary>
    /// Reads an embedded resource as a UTF-8 string.
    /// </summary>
    public static string ReadText(string resourceName)
    {
        using var stream = GetStream(resourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Extracts an embedded resource to a file on disk while computing its SHA-256 in
    /// the same pass (via <see cref="CryptoStream"/>), then re-hashes the on-disk bytes
    /// to confirm the write landed intact. Catches partial writes and AV interference
    /// without ever buffering the full payload in RAM.
    /// </summary>
    public static void ExtractToFile(string resourceName, string destPath)
    {
        string expectedHash;
        using (var source = GetStream(resourceName))
        using (var sha = SHA256.Create())
        using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var tee = new CryptoStream(fileStream, sha, CryptoStreamMode.Write))
        {
            source.CopyTo(tee);
            tee.FlushFinalBlock();
            expectedHash = Convert.ToHexString(sha.Hash!);
        }

        string actualHash = ResourceIntegrity.ComputeFileSha256(destPath);
        ResourceIntegrity.VerifyHash($"embedded resource {resourceName} -> {destPath}", expectedHash, actualHash);
    }

    private static Stream GetStream(string resourceName)
    {
        // Embedded resource names use the project default namespace + folder path with dots
        string fullName = $"Anomaly.InstallerCore.Resources.{resourceName}";
        var stream = Assembly.GetManifestResourceStream(fullName);
        if (stream == null)
            throw new InstallerException($"Embedded resource not found: {resourceName}");
        return stream;
    }
}
