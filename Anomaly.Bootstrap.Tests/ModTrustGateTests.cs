using System.Security.Cryptography;
using System.Text.Json;
using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class ModTrustGateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "anomaly-trust-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Approved_hash_remains_loadable()
    {
        var paths = CreateLayout();
        var dll = WriteAssembly(paths.Mods, "approved.dll", new byte[] { 1, 2, 3 });
        WritePolicy(paths.Policy, "enforce", Hash(dll));

        var result = Gate(paths).Apply();

        Assert.Empty(result.Failures);
        Assert.Single(result.Approved);
        Assert.True(File.Exists(dll));
    }

    [Fact]
    public void Unknown_hash_and_module_sidecar_are_quarantined()
    {
        var paths = CreateLayout();
        var dll = WriteAssembly(paths.Modules, "unknown.dll", new byte[] { 4, 5, 6 });
        File.WriteAllText(dll + ".anomaly.json", "{}");
        WritePolicy(paths.Policy, "enforce");

        var result = Gate(paths).Apply();

        var quarantined = Assert.Single(result.Quarantined);
        Assert.False(File.Exists(dll));
        Assert.NotNull(quarantined.QuarantinePath);
        Assert.True(File.Exists(quarantined.QuarantinePath));
        Assert.True(File.Exists(quarantined.QuarantinePath + ".anomaly.json"));
    }

    [Fact]
    public void Audit_mode_warns_without_moving()
    {
        var paths = CreateLayout();
        var dll = WriteAssembly(paths.Mods, "audit.dll", new byte[] { 7, 8, 9 });
        WritePolicy(paths.Policy, "audit");

        var result = Gate(paths).Apply();

        Assert.Empty(result.Quarantined);
        Assert.Contains(result.Warnings, warning => warning.Contains("full-trust", StringComparison.Ordinal));
        Assert.True(File.Exists(dll));
    }

    [Fact]
    public void Missing_policy_defaults_to_enforce_with_empty_approvals()
    {
        var paths = CreateLayout();
        var dll = WriteAssembly(paths.Mods, "unapproved.dll", new byte[] { 10, 11, 12 });

        var result = Gate(paths).Apply();

        Assert.Single(result.Quarantined);
        Assert.Contains(result.Warnings, warning => warning.Contains("No mod-trust policy", StringComparison.Ordinal));
        Assert.False(File.Exists(dll));
    }

    [Fact]
    public void Nested_assemblies_are_inventoried_before_load()
    {
        var paths = CreateLayout();
        var nested = Path.Combine(paths.Mods, "publisher", "package");
        Directory.CreateDirectory(nested);
        var dll = WriteAssembly(nested, "nested.dll", new byte[] { 13, 14, 15 });
        WritePolicy(paths.Policy, "enforce");

        var result = Gate(paths).Apply();

        Assert.Empty(result.Failures);
        Assert.Single(result.Quarantined);
        Assert.False(File.Exists(dll));
    }

    [Fact]
    public void Reparse_root_is_rejected_without_moving_external_assemblies()
    {
        var paths = CreateLayout();
        var outside = Path.Combine(_root, "outside-mods");
        Directory.CreateDirectory(outside);
        var externalDll = WriteAssembly(outside, "external.dll", new byte[] { 16, 17, 18 });
        Directory.Delete(paths.Mods);
        Directory.CreateSymbolicLink(paths.Mods, outside);
        WritePolicy(paths.Policy, "enforce");

        var result = Gate(paths).Apply();

        Assert.Contains(result.Failures, failure =>
            failure.Contains("Reparse-point mod directory", StringComparison.Ordinal));
        Assert.True(File.Exists(externalDll));
        Assert.Empty(result.Quarantined);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private (string Mods, string Modules, string Policy, string Quarantine) CreateLayout()
    {
        var mods = Path.Combine(_root, "Mods");
        var modules = Path.Combine(_root, "UserData", "Anomaly", "Modules");
        var policy = Path.Combine(_root, "UserData", "Anomaly", "mod-trust.json");
        var quarantine = Path.Combine(_root, "UserData", "Anomaly", "Quarantine");
        Directory.CreateDirectory(mods);
        Directory.CreateDirectory(modules);
        Directory.CreateDirectory(Path.GetDirectoryName(policy)!);
        return (mods, modules, policy, quarantine);
    }

    private static ModTrustGate Gate((string Mods, string Modules, string Policy, string Quarantine) paths) =>
        new(paths.Mods, paths.Modules, paths.Policy, paths.Quarantine);

    private static string WriteAssembly(string directory, string name, byte[] bytes)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void WritePolicy(string path, string mode, params string[] hashes)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(new ModTrustPolicy
        {
            Mode = mode,
            ApprovedSha256 = hashes.ToList(),
        }));
    }
}
