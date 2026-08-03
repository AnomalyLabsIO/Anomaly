using System.Security.Cryptography;
using System.Text.Json;

namespace Anomaly.Bootstrap;

/// <summary>
/// Applies a launcher-authored, hash-based trust policy before MelonLoader scans
/// the Mods directory. This is a pre-load consent gate, not a sandbox.
/// </summary>
public sealed class ModTrustGate
{
    private const long MaxPolicyBytes = 1024 * 1024;
    private readonly string _modsDirectory;
    private readonly string _moduleDirectory;
    private readonly string _policyPath;
    private readonly string _quarantineRoot;
    private readonly string _anomalyRoot;

    public ModTrustGate(string modsDirectory, string moduleDirectory, string policyPath, string quarantineRoot)
    {
        _modsDirectory = Path.GetFullPath(modsDirectory);
        _moduleDirectory = Path.GetFullPath(moduleDirectory);
        _policyPath = Path.GetFullPath(policyPath);
        _quarantineRoot = Path.GetFullPath(quarantineRoot);
        _anomalyRoot = Path.GetDirectoryName(_policyPath)
            ?? throw new ArgumentException("The trust policy must have a parent directory.", nameof(policyPath));
        EnsureContainedOrSame(_anomalyRoot, _quarantineRoot);
    }

    public ModTrustResult Apply()
    {
        var result = new ModTrustResult();
        var policy = ReadPolicy(result);
        var auditOnly = string.Equals(policy.Mode, "audit", StringComparison.OrdinalIgnoreCase);
        var approved = new HashSet<string>(policy.ApprovedSha256
            .Where(IsSha256)
            .Select(hash => hash.ToLowerInvariant()), StringComparer.Ordinal);

        if (!string.Equals(policy.Mode, "enforce", StringComparison.OrdinalIgnoreCase) && !auditOnly)
        {
            result.Failures.Add($"Unsupported mod trust mode '{policy.Mode}'.");
            return result;
        }

        EvaluateDirectory(_modsDirectory, includeManifestSidecar: false, auditOnly, approved, result);
        EvaluateDirectory(_moduleDirectory, includeManifestSidecar: true, auditOnly, approved, result);
        return result;
    }

    private ModTrustPolicy ReadPolicy(ModTrustResult result)
    {
        try
        {
            if (!File.Exists(_policyPath))
            {
                result.Warnings.Add("No mod-trust policy exists; enforce mode defaults to an empty approval set.");
                return new ModTrustPolicy();
            }

            EnsureDirectoryTreeNotReparse(_anomalyRoot, _anomalyRoot);
            EnsureFileNotReparse(_policyPath);
            var info = new FileInfo(_policyPath);
            if (info.Length <= 0 || info.Length > MaxPolicyBytes)
                throw new InvalidDataException("The mod-trust policy has an invalid size.");
            var policy = JsonSerializer.Deserialize<ModTrustPolicy>(File.ReadAllText(_policyPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (policy == null || policy.SchemaVersion != 1)
                throw new InvalidDataException("The mod-trust policy schema is unsupported.");
            policy.ApprovedSha256 ??= new List<string>();
            if (policy.ApprovedSha256.Any(hash => !IsSha256(hash)))
                throw new InvalidDataException("The mod-trust policy contains an invalid SHA-256 value.");
            return policy;
        }
        catch (Exception ex)
        {
            result.Failures.Add("Mod-trust policy rejected: " + ex.Message);
            return new ModTrustPolicy { Mode = "invalid" };
        }
    }

    private void EvaluateDirectory(
        string directory,
        bool includeManifestSidecar,
        bool auditOnly,
        ISet<string> approved,
        ModTrustResult result)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var candidate in EnumerateAssemblies(directory, result))
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                EnsureContained(directory, fullPath);
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse-point assemblies are not accepted.");

                var hash = ComputeSha256(fullPath);
                var artifact = new ModTrustArtifact(Path.GetFileName(fullPath), hash, fullPath);
                if (approved.Contains(hash))
                {
                    result.Approved.Add(artifact);
                    continue;
                }

                if (auditOnly)
                {
                    result.Warnings.Add($"Unapproved full-trust assembly would be quarantined in enforce mode: {artifact.FileName} ({hash}).");
                    continue;
                }

                var quarantineDirectory = Path.Combine(_quarantineRoot, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
                EnsureDirectoryTreeNotReparse(_anomalyRoot, _quarantineRoot);
                Directory.CreateDirectory(quarantineDirectory);
                EnsureDirectoryTreeNotReparse(_anomalyRoot, quarantineDirectory);
                var target = UniqueTarget(quarantineDirectory, Path.GetFileName(fullPath));
                string? sidecar = null;
                if (includeManifestSidecar)
                {
                    var candidateSidecar = fullPath + ".anomaly.json";
                    if (File.Exists(candidateSidecar))
                    {
                        EnsureFileNotReparse(candidateSidecar);
                        sidecar = candidateSidecar;
                    }
                }
                File.Move(fullPath, target);
                try
                {
                    if (sidecar != null)
                        File.Move(sidecar, target + ".anomaly.json");
                }
                catch
                {
                    if (!File.Exists(fullPath) && File.Exists(target))
                        File.Move(target, fullPath);
                    throw;
                }
                result.Quarantined.Add(new ModTrustArtifact(artifact.FileName, hash, fullPath, target));
            }
            catch (Exception ex)
            {
                result.Failures.Add($"Failed to contain '{Path.GetFileName(candidate)}': {ex.Message}");
            }
        }
    }

    private static IReadOnlyList<string> EnumerateAssemblies(string root, ModTrustResult result)
    {
        var assemblies = new List<string>();
        var pending = new Stack<string>();
        var fullRoot = Path.GetFullPath(root);
        try
        {
            var rootAttributes = File.GetAttributes(fullRoot);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                result.Failures.Add($"Reparse-point mod directory rejected: {Path.GetFileName(fullRoot)}.");
                return assemblies;
            }
        }
        catch (Exception ex)
        {
            result.Failures.Add($"Failed to inspect mod directory '{fullRoot}': {ex.Message}");
            return assemblies;
        }
        pending.Push(fullRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
                    assemblies.Add(file);

                foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        result.Failures.Add($"Reparse-point mod directory rejected: {Path.GetFileName(child)}.");
                        continue;
                    }
                    pending.Push(child);
                }
            }
            catch (Exception ex)
            {
                result.Failures.Add($"Failed to enumerate mod directory '{directory}': {ex.Message}");
            }
        }

        assemblies.Sort(StringComparer.OrdinalIgnoreCase);
        return assemblies;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static void EnsureContained(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate path escaped its module directory.");
    }

    private static void EnsureContainedOrSame(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase)) return;
        EnsureContained(normalizedRoot, normalizedPath);
    }

    private static void EnsureDirectoryTreeNotReparse(string root, string target)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        EnsureContainedOrSame(root, target);

        var current = root;
        InspectDirectoryIfPresent(current);
        var relative = Path.GetRelativePath(root, target);
        if (relative == ".") return;
        foreach (var segment in relative.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current)) return;
            InspectDirectoryIfPresent(current);
        }
    }

    private static void InspectDirectoryIfPresent(string path)
    {
        if (!Directory.Exists(path)) return;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Reparse-point trust directory rejected: {path}");
    }

    private static void EnsureFileNotReparse(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Reparse-point trust file rejected: {path}");
    }

    private static string UniqueTarget(string directory, string fileName)
    {
        var target = Path.Combine(directory, fileName);
        if (!File.Exists(target)) return target;
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(fileName) + "-" +
                                       Guid.NewGuid().ToString("N") + Path.GetExtension(fileName));
    }
}
