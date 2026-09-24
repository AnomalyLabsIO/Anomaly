using System.Text.Json.Serialization;

namespace Anomaly.ReleasePackaging;

internal sealed class PackageLayout
{
    public int SchemaVersion { get; set; }
    public List<PackageDefinition> Packages { get; set; } = new();
}

internal sealed class PackageDefinition
{
    public string PackageId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string ArchiveName { get; set; } = string.Empty;
    public List<PackageEntryDefinition> Entries { get; set; } = new();
    public RuntimeClosureDefinition? RuntimeClosure { get; set; }
}

internal sealed class PackageEntryDefinition
{
    public string Source { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? AssemblyVersion { get; set; }
}

internal sealed class RuntimeClosureDefinition
{
    public string RuntimeFramework { get; set; } = string.Empty;
    public List<string> ReferenceProjects { get; set; } = new();
    public List<string> FrameworkProvidedAssemblies { get; set; } = new();
    public List<HostProvidedAssemblyDefinition> HostProvidedAssemblies { get; set; } = new();
}

internal sealed class HostProvidedAssemblyDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? AssemblyVersion { get; set; }
    public string? FileVersion { get; set; }
}

internal sealed class DependencyInventory
{
    public int SchemaVersion { get; set; } = 2;
    public string SourceCommit { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Configuration { get; set; } = "Release";
    public string Platform { get; set; } = "x64";
    public string LayoutSha256 { get; set; } = string.Empty;
    public string ThirdPartyDeclarationSha256 { get; set; } = string.Empty;
    public List<ResolvedInput> ResolvedInputs { get; set; } = new();
    public List<PackageInventory> Packages { get; set; } = new();
}

internal sealed class ResolvedInput
{
    public string Project { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;
    public string AssemblyVersion { get; set; } = string.Empty;
    public string FileVersion { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class PackageInventory
{
    public string PackageId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string ArchiveName { get; set; } = string.Empty;
    public long ArchiveSizeBytes { get; set; }
    public string ArchiveSha256 { get; set; } = string.Empty;
    public List<PackageInventoryEntry> Entries { get; set; } = new();
    public RuntimeDependencyClosureInventory? RuntimeClosure { get; set; }
}

internal sealed class PackageInventoryEntry
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class RuntimeDependencyClosureInventory
{
    public string RuntimeFramework { get; set; } = string.Empty;
    public List<PackagedAssemblyInventory> PackagedAssemblies { get; set; } = new();
    public List<string> FrameworkProvidedAssemblies { get; set; } = new();
    public List<HostProvidedAssemblyInventory> HostProvidedAssemblies { get; set; } = new();
    public List<AssemblyDependencyEdge> Dependencies { get; set; } = new();
}

internal sealed class PackagedAssemblyInventory
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class HostProvidedAssemblyInventory
{
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
}

internal sealed class AssemblyDependencyEdge
{
    public string Assembly { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string RequestedVersion { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
}

internal sealed class ReleaseReceipt
{
    public int SchemaVersion { get; set; } = 3;
    public string SourceCommit { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public long SourceDateEpoch { get; set; }
    public DateTimeOffset GeneratedUtc { get; set; }
    public string DotnetRuntime { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string DependencyInventorySha256 { get; set; } = string.Empty;
    public List<ReceiptPackage> Packages { get; set; } = new();
    public List<string> Commands { get; set; } = new();
    public ReceiptSigning Signing { get; set; } = new();
    public ReceiptGateEvidence? GateEvidence { get; set; }
}

internal sealed class ReceiptPackage
{
    public string PackageId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string ArchiveName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class ReceiptSigning
{
    public string State { get; set; } = "unsigned";
    public string? KeyId { get; set; }
    public List<string> TrustedKeyIds { get; set; } = new();
    public List<ReceiptEvidenceFile> Files { get; set; } = new();
}

internal sealed class ReceiptGateEvidence
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CompletedUtc { get; set; }
    public string SummaryPath { get; set; } = string.Empty;
    public List<ReceiptToolVersion> ToolVersions { get; set; } = new();
    public List<ReceiptGateResult> Gates { get; set; } = new();
    public List<ReceiptEvidenceFile> Files { get; set; } = new();
}

internal sealed class ReceiptToolVersion
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

internal sealed class ReceiptGateResult
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    public string LogPath { get; set; } = string.Empty;
    public List<string> ResultPaths { get; set; } = new();
}

internal sealed class ReceiptEvidenceFile
{
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PackageLayout))]
[JsonSerializable(typeof(DependencyInventory))]
[JsonSerializable(typeof(RuntimeDependencyClosureInventory))]
[JsonSerializable(typeof(ReleaseReceipt))]
[JsonSerializable(typeof(ReceiptGateEvidence))]
internal partial class ReleaseJsonContext : JsonSerializerContext;
