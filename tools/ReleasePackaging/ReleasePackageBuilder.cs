using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Anomaly.ReleasePackaging;

internal static partial class ReleasePackageBuilder
{
    private const string LayoutRelativePath = "release/package-layout.json";
    private const string ThirdPartyRelativePath = "THIRD_PARTY_DEPENDENCIES.md";
    private static readonly string[] ReferenceProjects =
    [
        "Anomaly.Bootstrap",
        "Anomaly.Client",
        "Anomaly.Client.Api",
        "Anomaly.Client.Scripting",
        "Anomaly.Client.ScriptWorker",
        "Anomaly.AssetValidation",
        "Anomaly.Server",
        "Anomaly.Shared",
    ];

    internal static void Create(
        string repositoryRoot,
        string outputDirectory,
        string version,
        string sourceCommit,
        string sourceDateEpoch,
        string referenceDirectory)
    {
        var repository = Path.GetFullPath(repositoryRoot);
        var output = Path.GetFullPath(outputDirectory);
        var references = Path.GetFullPath(referenceDirectory);
        ValidateVersion(version);
        ValidateSourceCommit(sourceCommit);
        if (!long.TryParse(sourceDateEpoch, out var epochSeconds) || epochSeconds < 0)
            throw new InvalidDataException("source-date-epoch must be a non-negative Unix timestamp.");
        var generatedUtc = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        var zipTimestamp = ClampZipTimestamp(generatedUtc);

        if (!Directory.Exists(repository))
            throw new DirectoryNotFoundException($"Repository root not found: {repository}");
        if (!Directory.Exists(references))
            throw new DirectoryNotFoundException($"Resolved-reference directory not found: {references}");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException($"Release output directory must be absent or empty: {output}");
        Directory.CreateDirectory(output);

        var layoutPath = ResolveUnderRoot(repository, LayoutRelativePath);
        var thirdPartyPath = ResolveUnderRoot(repository, ThirdPartyRelativePath);
        var layoutBytes = File.ReadAllBytes(layoutPath);
        var layout = JsonSerializer.Deserialize(layoutBytes, ReleaseJsonContext.Default.PackageLayout)
                     ?? throw new InvalidDataException("Package layout is empty.");
        ValidateLayout(layout);

        var inventory = new DependencyInventory
        {
            SourceCommit = sourceCommit,
            Version = version,
            LayoutSha256 = HashBytes(layoutBytes),
            ThirdPartyDeclarationSha256 = HashFile(thirdPartyPath),
            ResolvedInputs = ReadResolvedInputs(references),
        };

        var payloadRoot = Path.Combine(output, "payloads");
        var archiveRoot = Path.Combine(output, "archives");
        Directory.CreateDirectory(payloadRoot);
        Directory.CreateDirectory(archiveRoot);

        foreach (var package in layout.Packages.OrderBy(definition => definition.PackageId, StringComparer.Ordinal))
        {
            var packageRoot = Path.Combine(payloadRoot, package.Target);
            Directory.CreateDirectory(packageRoot);
            var packageInventory = new PackageInventory
            {
                PackageId = package.PackageId,
                Target = package.Target,
                ArchiveName = package.ArchiveName.Replace("{version}", version, StringComparison.Ordinal),
            };

            foreach (var definition in package.Entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
            {
                var source = ResolveUnderRoot(repository, definition.Source);
                ValidateRegularFile(source, $"Package source '{definition.Source}'");
                var destination = ResolveUnderRoot(packageRoot, definition.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, false);
                File.SetLastWriteTimeUtc(destination, generatedUtc.UtcDateTime);
                var info = new FileInfo(destination);
                packageInventory.Entries.Add(new PackageInventoryEntry
                {
                    Path = NormalizeRelativePath(definition.Path),
                    Name = Path.GetFileName(definition.Path),
                    SizeBytes = info.Length,
                    Sha256 = HashFile(destination),
                });
            }

            if (package.Target == "client-runtime")
                ValidateSkinContentPack(ResolveUnderRoot(packageRoot, ContentPackDirectory + "/personal/skins"));

            packageInventory.RuntimeClosure = BuildAssemblyClosure(
                package, packageRoot, inventory.ResolvedInputs, package.RuntimeClosure, "runtime");

            var archivePath = Path.Combine(archiveRoot, packageInventory.ArchiveName);
            CreateArchive(archivePath, packageRoot, packageInventory.Entries, zipTimestamp);
            var archiveInfo = new FileInfo(archivePath);
            packageInventory.ArchiveSizeBytes = archiveInfo.Length;
            packageInventory.ArchiveSha256 = HashFile(archivePath);
            inventory.Packages.Add(packageInventory);
        }

        var copiedLayoutPath = Path.Combine(output, "package-layout.json");
        WriteAtomically(copiedLayoutPath, layoutBytes);
        var inventoryPath = Path.Combine(output, "dependency-inventory.json");
        var inventoryBytes = JsonSerializer.SerializeToUtf8Bytes(
            inventory, ReleaseJsonContext.Default.DependencyInventory);
        WriteAtomically(inventoryPath, inventoryBytes);

        var receipt = new ReleaseReceipt
        {
            SourceCommit = sourceCommit,
            Version = version,
            SourceDateEpoch = epochSeconds,
            GeneratedUtc = generatedUtc,
            DotnetRuntime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            DependencyInventorySha256 = HashBytes(inventoryBytes),
            Packages = inventory.Packages.Select(package => new ReceiptPackage
            {
                PackageId = package.PackageId,
                Target = package.Target,
                ArchiveName = package.ArchiveName,
                SizeBytes = package.ArchiveSizeBytes,
                Sha256 = package.ArchiveSha256,
            }).ToList(),
            Commands =
            [
                "dotnet build Anomaly.slnx -c Release -p:DeployLocal=false -p:ContinuousIntegrationBuild=true -warnaserror --nologo",
                "dotnet msbuild <runtime-project> -t:WriteAnomalyResolvedReferences -p:Configuration=Release",
                "dotnet run --project tools/ReleasePackaging -- create ...",
                "dotnet run --project tools/ReleasePackaging -- verify ...",
            ],
            Signing = new ReceiptSigning
            {
                State = "unsigned",
                KeyId = null,
                TrustedKeyIds = [],
                Files = [],
            },
        };
        WriteAtomically(
            Path.Combine(output, "release-receipt.json"),
            JsonSerializer.SerializeToUtf8Bytes(receipt, ReleaseJsonContext.Default.ReleaseReceipt));

        Verify(output);
        Console.WriteLine($"Created and verified {inventory.Packages.Count} deterministic release package(s) in {output}.");
    }

    internal static void Verify(string outputDirectory)
    {
        var output = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(output))
            throw new DirectoryNotFoundException($"Release output directory not found: {output}");

        var layoutBytes = File.ReadAllBytes(Path.Combine(output, "package-layout.json"));
        var layout = JsonSerializer.Deserialize(layoutBytes, ReleaseJsonContext.Default.PackageLayout)
                     ?? throw new InvalidDataException("Packaged layout is empty.");
        ValidateLayout(layout);
        var inventoryBytes = File.ReadAllBytes(Path.Combine(output, "dependency-inventory.json"));
        var inventory = JsonSerializer.Deserialize(inventoryBytes, ReleaseJsonContext.Default.DependencyInventory)
                        ?? throw new InvalidDataException("Dependency inventory is empty.");
        var receipt = JsonSerializer.Deserialize(
                          File.ReadAllBytes(Path.Combine(output, "release-receipt.json")),
                          ReleaseJsonContext.Default.ReleaseReceipt)
                      ?? throw new InvalidDataException("Release receipt is empty.");

        if (inventory.SchemaVersion != 2 || receipt.SchemaVersion != 3 ||
            !string.Equals(inventory.LayoutSha256, HashBytes(layoutBytes), StringComparison.Ordinal) ||
            !string.Equals(receipt.DependencyInventorySha256, HashBytes(inventoryBytes), StringComparison.Ordinal) ||
            !string.Equals(inventory.SourceCommit, receipt.SourceCommit, StringComparison.Ordinal) ||
            !string.Equals(inventory.Version, receipt.Version, StringComparison.Ordinal))
        {
            throw new CryptographicException("Release metadata hashes or identities do not match.");
        }

        ValidateSigningEvidence(output, inventory, receipt.Signing);
        if (receipt.GateEvidence is not null)
            ValidateGateEvidence(output, receipt.GateEvidence, requireRecordedFiles: true);

        var expectedPackages = layout.Packages.ToDictionary(package => package.PackageId, StringComparer.Ordinal);
        if (inventory.Packages.Count != expectedPackages.Count || receipt.Packages.Count != expectedPackages.Count)
            throw new InvalidDataException("Release package inventory is incomplete.");

        var zipTimestamp = ClampZipTimestamp(receipt.GeneratedUtc);
        foreach (var package in inventory.Packages)
        {
            if (!expectedPackages.TryGetValue(package.PackageId, out var definition) ||
                !string.Equals(package.Target, definition.Target, StringComparison.Ordinal))
                throw new InvalidDataException($"Unexpected package identity: {package.PackageId}/{package.Target}.");

            var receiptPackage = receipt.Packages.SingleOrDefault(candidate =>
                string.Equals(candidate.PackageId, package.PackageId, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"Receipt is missing package '{package.PackageId}'.");
            var archivePath = Path.Combine(output, "archives", package.ArchiveName);
            ValidateRegularFile(archivePath, $"Archive '{package.ArchiveName}'");
            var archiveInfo = new FileInfo(archivePath);
            var archiveHash = HashFile(archivePath);
            if (archiveInfo.Length != package.ArchiveSizeBytes ||
                archiveInfo.Length != receiptPackage.SizeBytes ||
                !string.Equals(archiveHash, package.ArchiveSha256, StringComparison.Ordinal) ||
                !string.Equals(archiveHash, receiptPackage.Sha256, StringComparison.Ordinal))
            {
                throw new CryptographicException($"Archive '{package.ArchiveName}' does not match its receipt.");
            }

            var payloadRoot = Path.Combine(output, "payloads", package.Target);
            var expectedEntries = package.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
            if (!expectedEntries.TryGetValue(ThirdPartyRelativePath, out var thirdPartyDeclaration) ||
                !string.Equals(
                    thirdPartyDeclaration.Sha256,
                    inventory.ThirdPartyDeclarationSha256,
                    StringComparison.Ordinal))
            {
                throw new CryptographicException(
                    $"Package '{package.PackageId}' does not carry the dependency declaration bound by the inventory.");
            }
            var payloadFiles = Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(Path.GetRelativePath(payloadRoot, path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            if (!payloadFiles.SequenceEqual(expectedEntries.Keys.OrderBy(path => path, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidDataException($"Payload '{package.Target}' has missing or unexpected files.");

            foreach (var entry in package.Entries)
            {
                var payloadPath = ResolveUnderRoot(payloadRoot, entry.Path);
                var info = new FileInfo(payloadPath);
                if (info.Length != entry.SizeBytes ||
                    !string.Equals(HashFile(payloadPath), entry.Sha256, StringComparison.Ordinal))
                    throw new CryptographicException($"Payload entry '{entry.Path}' does not match its inventory.");
            }

            var expectedRuntimeClosure = BuildAssemblyClosure(
                definition, payloadRoot, inventory.ResolvedInputs, definition.RuntimeClosure, "runtime");
            if (!AssemblyClosuresMatch(expectedRuntimeClosure, package.RuntimeClosure))
                throw new CryptographicException($"Runtime dependency closure mismatch for '{package.PackageId}'.");
            VerifyArchive(archivePath, expectedEntries, zipTimestamp);
        }

        Console.WriteLine($"Verified release output: {output}");
    }

    internal static void FinalizeGateEvidence(string outputDirectory, string summaryPath)
    {
        var output = Path.GetFullPath(outputDirectory);
        Verify(output);

        var receiptPath = Path.Combine(output, "release-receipt.json");
        var receipt = JsonSerializer.Deserialize(
                          File.ReadAllBytes(receiptPath),
                          ReleaseJsonContext.Default.ReleaseReceipt)
                      ?? throw new InvalidDataException("Release receipt is empty.");
        if (receipt.GateEvidence is not null)
            throw new InvalidOperationException("Release gate evidence has already been finalized.");
        if (!string.Equals(receipt.Signing.State, "unsigned", StringComparison.Ordinal) ||
            receipt.Signing.KeyId is not null || receipt.Signing.TrustedKeyIds.Count != 0)
            throw new InvalidOperationException("Only an unsigned release output can receive complete-gate evidence.");

        var fullSummaryPath = Path.GetFullPath(summaryPath);
        var relativeSummaryPath = NormalizeRelativePath(Path.GetRelativePath(output, fullSummaryPath));
        ValidateRelativePath(relativeSummaryPath, "gate evidence summary path");
        if (!relativeSummaryPath.StartsWith("evidence/", StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(relativeSummaryPath), "gate-summary.json", StringComparison.Ordinal))
            throw new InvalidDataException("Gate evidence summary must be output/evidence/gate-summary.json.");
        ValidateRegularFile(fullSummaryPath, "Gate evidence summary");

        var evidence = JsonSerializer.Deserialize(
                           File.ReadAllBytes(fullSummaryPath),
                           ReleaseJsonContext.Default.ReceiptGateEvidence)
                       ?? throw new InvalidDataException("Gate evidence summary is empty.");
        evidence.SummaryPath = relativeSummaryPath;
        evidence.Files = [];
        ValidateGateEvidence(output, evidence, requireRecordedFiles: false);

        var expectedPaths = GetExpectedGateEvidencePaths(evidence);
        var evidenceRoot = ResolveUnderRoot(output, "evidence");
        RejectReparsePoints(evidenceRoot, "Gate evidence");
        var actualPaths = Directory.EnumerateFiles(evidenceRoot, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(output, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        if (!actualPaths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
            throw new InvalidDataException("Gate evidence contains missing or unexpected files.");

        evidence.Files = actualPaths.Select(path =>
        {
            var fullPath = ResolveUnderRoot(output, path);
            ValidateRegularFile(fullPath, $"Gate evidence file '{path}'");
            return new ReceiptEvidenceFile
            {
                Path = path,
                SizeBytes = new FileInfo(fullPath).Length,
                Sha256 = HashFile(fullPath),
            };
        }).ToList();
        receipt.GateEvidence = evidence;
        WriteAtomically(
            receiptPath,
            JsonSerializer.SerializeToUtf8Bytes(receipt, ReleaseJsonContext.Default.ReleaseReceipt));
        Verify(output);
        Console.WriteLine($"Finalized complete-gate evidence in {receiptPath}.");
    }

    internal static void RecordSigning(string outputDirectory, string keyId, string publicKeyPath)
    {
        var output = Path.GetFullPath(outputDirectory);
        ValidateKeyId(keyId);
        var fullPublicKeyPath = Path.GetFullPath(publicKeyPath);
        ValidateRegularFile(fullPublicKeyPath, "Release verification key");
        using var verifier = ECDsa.Create();
        verifier.ImportFromPem(File.ReadAllText(fullPublicKeyPath));
        EnsureP256(verifier, "Release verification key");
        Verify(output);

        var inventory = JsonSerializer.Deserialize(
                            File.ReadAllBytes(Path.Combine(output, "dependency-inventory.json")),
                            ReleaseJsonContext.Default.DependencyInventory)
                        ?? throw new InvalidDataException("Dependency inventory is empty.");
        var receiptPath = Path.Combine(output, "release-receipt.json");
        var receipt = JsonSerializer.Deserialize(
                          File.ReadAllBytes(receiptPath),
                          ReleaseJsonContext.Default.ReleaseReceipt)
                      ?? throw new InvalidDataException("Release receipt is empty.");
        if (!string.Equals(receipt.Signing.State, "unsigned", StringComparison.Ordinal) ||
            receipt.Signing.KeyId is not null || receipt.Signing.TrustedKeyIds.Count != 0 ||
            receipt.Signing.Files.Count != 0)
            throw new InvalidOperationException("Release signing evidence has already been recorded.");

        var signingFiles = GetExpectedSigningPaths(inventory);
        ValidateSignedManifestSet(output, inventory, keyId, signingFiles, verifier);
        receipt.Signing = new ReceiptSigning
        {
            State = "signed",
            KeyId = keyId,
            TrustedKeyIds = [keyId],
            Files = signingFiles.Select(path =>
            {
                var fullPath = ResolveUnderRoot(output, path);
                return new ReceiptEvidenceFile
                {
                    Path = path,
                    SizeBytes = new FileInfo(fullPath).Length,
                    Sha256 = HashFile(fullPath),
                };
            }).ToList(),
        };
        WriteAtomically(
            receiptPath,
            JsonSerializer.SerializeToUtf8Bytes(receipt, ReleaseJsonContext.Default.ReleaseReceipt));
        Verify(output);
        Console.WriteLine($"Recorded non-secret signing evidence for keyId '{keyId}'.");
    }

    internal static void AuditRuntimeClosures(string outputDirectory)
    {
        var output = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(output))
            throw new DirectoryNotFoundException($"Release output directory not found: {output}");
        var layout = JsonSerializer.Deserialize(
                         File.ReadAllBytes(Path.Combine(output, "package-layout.json")),
                         ReleaseJsonContext.Default.PackageLayout)
                     ?? throw new InvalidDataException("Packaged layout is empty.");
        var inventory = JsonSerializer.Deserialize(
                            File.ReadAllBytes(Path.Combine(output, "dependency-inventory.json")),
                            ReleaseJsonContext.Default.DependencyInventory)
                        ?? throw new InvalidDataException("Dependency inventory is empty.");
        ValidateLayout(layout);
        foreach (var package in layout.Packages.Where(item => item.RuntimeClosure is not null))
        {
            _ = BuildAssemblyClosure(
                package,
                Path.Combine(output, "payloads", package.Target),
                inventory.ResolvedInputs,
                package.RuntimeClosure,
                "runtime");
        }
        Console.WriteLine($"Audited runtime dependency closure: {output}");
    }

    private static List<ResolvedInput> ReadResolvedInputs(string referenceDirectory)
    {
        var inputs = new List<ResolvedInput>();
        foreach (var project in ReferenceProjects)
        {
            var listPath = Path.Combine(referenceDirectory, project + ".refs");
            ValidateRegularFile(listPath, $"Resolved-reference list for {project}");
            var byName = new Dictionary<string, ResolvedInput>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(listPath))
            {
                var path = line.Trim().TrimStart('\uFEFF');
                if (path.Length == 0)
                    continue;
                path = Path.GetFullPath(path);
                ValidateRegularFile(path, $"Resolved reference for {project}");
                AssemblyName? identity;
                try
                {
                    identity = AssemblyName.GetAssemblyName(path);
                }
                catch (BadImageFormatException)
                {
                    identity = null;
                }
                var input = new ResolvedInput
                {
                    Project = project,
                    Name = Path.GetFileName(path),
                    AssemblyName = identity?.Name ?? string.Empty,
                    AssemblyVersion = identity?.Version?.ToString() ?? string.Empty,
                    FileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty,
                    SizeBytes = new FileInfo(path).Length,
                    Sha256 = HashFile(path),
                };
                if (byName.TryGetValue(input.Name, out var existing))
                {
                    if (existing.SizeBytes != input.SizeBytes ||
                        !string.Equals(existing.Sha256, input.Sha256, StringComparison.Ordinal))
                        throw new InvalidDataException($"{project} resolved multiple different '{input.Name}' assemblies.");
                    continue;
                }
                byName.Add(input.Name, input);
            }
            if (byName.Count == 0)
                throw new InvalidDataException($"Resolved-reference list for {project} is empty.");
            inputs.AddRange(byName.Values.OrderBy(input => input.Name, StringComparer.Ordinal));
        }
        return inputs.OrderBy(input => input.Project, StringComparer.Ordinal)
            .ThenBy(input => input.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static RuntimeDependencyClosureInventory? BuildAssemblyClosure(
        PackageDefinition package,
        string packageRoot,
        IReadOnlyList<ResolvedInput> resolvedInputs,
        RuntimeClosureDefinition? definition,
        string closureKind)
    {
        if (definition is null)
            return null;

        var packagedAssemblies = new Dictionary<string, ManagedAssemblyMetadata>(StringComparer.OrdinalIgnoreCase);
        var packagedVersionPins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in package.Entries
                     .Where(entry => entry.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            var path = ResolveUnderRoot(packageRoot, entry.Path);
            var metadata = ReadManagedAssembly(path, NormalizeRelativePath(entry.Path));
            if (entry.AssemblyVersion is not null)
            {
                if (!string.Equals(metadata.Version, entry.AssemblyVersion, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"{closureKind} package '{package.PackageId}' has assembly '{metadata.Name}' version " +
                        $"{metadata.Version}; expected {entry.AssemblyVersion}.");
                packagedVersionPins.Add(metadata.Name, entry.AssemblyVersion);
            }
            if (!packagedAssemblies.TryAdd(metadata.Name, metadata))
                throw new InvalidDataException(
                    $"Package '{package.PackageId}' contains multiple assemblies named '{metadata.Name}'.");
        }
        if (packagedAssemblies.Count == 0)
            throw new InvalidDataException(
                $"{closureKind} package '{package.PackageId}' contains no managed assemblies.");

        var frameworkNames = definition.FrameworkProvidedAssemblies
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hostDefinitions = definition.HostProvidedAssemblies
            .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var referencedFramework = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referencedHost = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<AssemblyDependencyEdge>();

        foreach (var assembly in packagedAssemblies.Values.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            foreach (var reference in assembly.References
                         .OrderBy(item => item.Name, StringComparer.Ordinal)
                         .ThenBy(item => item.Version, StringComparer.Ordinal))
            {
                if ((assembly.Name is "Anomaly.Client.ScriptWorker" or "Anomaly.MoonSharp.Interpreter") &&
                    !frameworkNames.Contains(reference.Name) &&
                    !string.Equals(reference.Name, "Anomaly.MoonSharp.Interpreter", StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Isolated worker dependency '{assembly.Name}' must not reference game or host assembly '{reference.Name}'.");
                string resolution;
                if (packagedAssemblies.TryGetValue(reference.Name, out var packagedDependency))
                {
                    resolution = "packaged";
                    if (packagedVersionPins.ContainsKey(reference.Name) &&
                        !string.Equals(reference.Version, packagedDependency.Version, StringComparison.Ordinal))
                        throw new InvalidDataException(
                            $"{closureKind} package '{package.PackageId}' has '{assembly.Name}' requesting " +
                            $"'{reference.Name}' version {reference.Version}, but packages version " +
                            $"{packagedDependency.Version}.");
                }
                else if (frameworkNames.Contains(reference.Name))
                {
                    resolution = "framework";
                    referencedFramework.Add(reference.Name);
                }
                else if (hostDefinitions.TryGetValue(reference.Name, out var hostDefinition))
                {
                    resolution = "host";
                    referencedHost.Add(reference.Name);
                    if (hostDefinition.AssemblyVersion is not null &&
                        !string.Equals(reference.Version, hostDefinition.AssemblyVersion, StringComparison.Ordinal))
                        throw new InvalidDataException(
                            $"{closureKind} package '{package.PackageId}' has '{assembly.Name}' requesting " +
                            $"host-provided '{reference.Name}' version {reference.Version}; expected " +
                            $"{hostDefinition.AssemblyVersion}.");
                }
                else
                {
                    throw new InvalidDataException(
                        $"{closureKind} package '{package.PackageId}' is missing dependency '{reference.Name}' " +
                        $"requested by '{assembly.Name}'. Add the DLL to the package or declare its exact provider.");
                }

                edges.Add(new AssemblyDependencyEdge
                {
                    Assembly = assembly.Name,
                    Reference = reference.Name,
                    RequestedVersion = reference.Version,
                    Resolution = resolution,
                });
            }
        }

        var unusedFramework = frameworkNames.Except(referencedFramework, StringComparer.OrdinalIgnoreCase).ToList();
        var unusedHost = hostDefinitions.Keys.Except(referencedHost, StringComparer.OrdinalIgnoreCase).ToList();
        if (unusedFramework.Count != 0 || unusedHost.Count != 0)
        {
            throw new InvalidDataException(
                $"{closureKind} closure declarations for '{package.PackageId}' are stale. " +
                $"Unused framework entries: [{string.Join(", ", unusedFramework.Order(StringComparer.Ordinal))}]. " +
                $"Unused host entries: [{string.Join(", ", unusedHost.Order(StringComparer.Ordinal))}].");
        }

        var referenceProjects = definition.ReferenceProjects.ToHashSet(StringComparer.Ordinal);
        foreach (var hostName in referencedHost)
        {
            var matchingInputs = resolvedInputs.Where(input =>
                    referenceProjects.Contains(input.Project) &&
                    string.Equals(input.AssemblyName, hostName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matchingInputs.Length == 0)
            {
                throw new InvalidDataException(
                    $"Host-provided dependency '{hostName}' for '{package.PackageId}' was not present in " +
                    "the captured resolved-reference inputs.");
            }
            var expectedVersion = hostDefinitions[hostName].AssemblyVersion;
            var expectedFileVersion = hostDefinitions[hostName].FileVersion;
            if (expectedVersion is not null || expectedFileVersion is not null)
            {
                foreach (var input in matchingInputs)
                {
                    if (expectedVersion is not null &&
                        (!string.Equals(input.AssemblyName, hostName, StringComparison.OrdinalIgnoreCase) ||
                         !string.Equals(input.AssemblyVersion, expectedVersion, StringComparison.Ordinal)))
                        throw new InvalidDataException(
                            $"Resolved input '{input.Name}' for '{input.Project}' has assembly identity " +
                            $"'{input.AssemblyName}, Version={input.AssemblyVersion}'; expected " +
                            $"'{hostName}, Version={expectedVersion}' for '{package.PackageId}'.");
                    if (expectedFileVersion is not null &&
                        !string.Equals(input.FileVersion, expectedFileVersion, StringComparison.Ordinal))
                        throw new InvalidDataException(
                            $"Resolved input '{input.Name}' for '{input.Project}' has file version " +
                            $"'{input.FileVersion}'; expected '{expectedFileVersion}' for '{package.PackageId}'.");
                }
            }
        }

        return new RuntimeDependencyClosureInventory
        {
            RuntimeFramework = definition.RuntimeFramework,
            PackagedAssemblies = packagedAssemblies.Values
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => new PackagedAssemblyInventory
                {
                    Name = item.Name,
                    Version = item.Version,
                    Path = item.Path,
                    SizeBytes = item.SizeBytes,
                    Sha256 = item.Sha256,
                })
                .ToList(),
            FrameworkProvidedAssemblies = referencedFramework.Order(StringComparer.Ordinal).ToList(),
            HostProvidedAssemblies = referencedHost
                .Order(StringComparer.Ordinal)
                .Select(name => new HostProvidedAssemblyInventory
                {
                    Name = hostDefinitions[name].Name,
                    Provider = hostDefinitions[name].Provider,
                })
                .ToList(),
            Dependencies = edges
                .OrderBy(edge => edge.Assembly, StringComparer.Ordinal)
                .ThenBy(edge => edge.Reference, StringComparer.Ordinal)
                .ThenBy(edge => edge.RequestedVersion, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private static ManagedAssemblyMetadata ReadManagedAssembly(string path, string packagePath)
    {
        ValidateRegularFile(path, $"Managed package entry '{packagePath}'");
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata)
            throw new InvalidDataException($"Package DLL is not a managed assembly: {packagePath}");
        var metadata = peReader.GetMetadataReader();
        if (!metadata.IsAssembly)
            throw new InvalidDataException($"Package DLL has no assembly manifest: {packagePath}");

        var definition = metadata.GetAssemblyDefinition();
        return new ManagedAssemblyMetadata(
            metadata.GetString(definition.Name),
            definition.Version.ToString(),
            packagePath,
            new FileInfo(path).Length,
            HashFile(path),
            metadata.AssemblyReferences
                .Select(handle => metadata.GetAssemblyReference(handle))
                .Select(reference => new ManagedAssemblyReference(
                    metadata.GetString(reference.Name), reference.Version.ToString()))
                .ToList());
    }

    private static bool AssemblyClosuresMatch(
        RuntimeDependencyClosureInventory? expected,
        RuntimeDependencyClosureInventory? actual)
    {
        if (expected is null || actual is null)
            return expected is null && actual is null;
        var expectedBytes = JsonSerializer.SerializeToUtf8Bytes(
            expected, ReleaseJsonContext.Default.RuntimeDependencyClosureInventory);
        var actualBytes = JsonSerializer.SerializeToUtf8Bytes(
            actual, ReleaseJsonContext.Default.RuntimeDependencyClosureInventory);
        return expectedBytes.AsSpan().SequenceEqual(actualBytes);
    }

    private static void ValidateLayout(PackageLayout layout)
    {
        if (layout.SchemaVersion != 2 || layout.Packages.Count != 3)
            throw new InvalidDataException("Package layout schema or package count is invalid.");
        var required = new Dictionary<string, (string Target, string[] Paths)>(StringComparer.Ordinal)
        {
            ["anomaly-client"] = ("client-runtime",
            [
                "UserData/Anomaly/ContentPacks/personal/skins/pack.json",
                "UserData/Anomaly/ContentPacks/personal/skins/assets/personal-skins.bundle",
                "UserData/Anomaly/ContentPacks/personal/skins/assets/personal-skins.index.json",
                "Plugins/Anomaly.Bootstrap.dll",
                "Mods/Anomaly.Client.dll",
                "UserLibs/Anomaly.Client.Api.dll",
                "UserLibs/Anomaly.Client.Scripting.dll",
                "UserLibs/Anomaly.MoonSharp.Interpreter.dll",
                "UserLibs/Anomaly.Client.ScriptWorker.dll",
                "UserLibs/Anomaly.Client.ScriptWorker.deps.json",
                "UserLibs/Anomaly.Client.ScriptWorker.runtimeconfig.json",
                "UserLibs/Anomaly.Shared.dll",
                "UserLibs/BouncyCastle.Cryptography.dll",
                "UserLibs/Anomaly.AssetValidation.dll",
                "UserLibs/AssetsTools.NET.dll",
                "licenses/AssetsTools.NET.md",
                "licenses/MoonSharp.LICENSE",
                "licenses/MoonSharp.md",
                "THIRD_PARTY_DEPENDENCIES.md",
                "LICENSE",
                "NOTICE",
            ]),
            ["anomaly-server"] = ("server-runtime",
            [
                "plugins/global/Anomaly.Server.dll",
                "dependencies/global/Anomaly.Shared.dll",
                "dependencies/global/0Harmony.dll",
                "THIRD_PARTY_DEPENDENCIES.md",
                "LICENSE",
                "NOTICE",
            ]),
            ["anomaly-sdk"] = ("developer-sdk",
            [
                "lib/net6.0/Anomaly.Client.Api.dll",
                "lib/net48/Anomaly.Shared.dll",
                "lib/net6.0/Anomaly.Client.Api.xml",
                "lib/net48/Anomaly.Shared.xml",
                "lib/net48/Anomaly.Server.dll",
                "lib/net48/Anomaly.Server.xml",
                "samples/Anomaly.Server.Sample.props",
                "samples/HelloAnomalyServer/HelloAnomalyServer.csproj",
                "samples/HelloAnomalyServer/Plugin.cs",
                "samples/HelloAnomalyServer/ContentPacks/example/panel/pack.json",
                "samples/HelloAnomalyServer/ContentPacks/example/panel/scripts/main.lua",
                "samples/HelloAnomalyServer/ContentPacks/example/panel/scripts/messages.json",
                "samples/HelloAnomalyServer/README.md",
                "docs/PLAYER_POSES.md",
                "docs/COMBAT_POSES.md",
                "Anomaly.Server/MODDING.md",
                "Anomaly.Server/PLAYER_POSES.md",
                "Anomaly.Server/COMBAT_POSES.md",
                "Anomaly.Client.Scripting/README.md",
                "Anomaly.Client.Scripting/SCRIPT-PACKS.md",
                "Anomaly.Client.Scripting/MESSAGING-DESIGN.md",
                "Anomaly.Client.Scripting/CAPABILITIES.md",
                "Anomaly.Client.Scripting/CONNECTION-LIFECYCLE.md",
                "Anomaly.Client.Scripting/ALLOCATION-AUDIT.md",
                "Anomaly.Client.Scripting/ScriptAPI.Shipped.txt",
                "Anomaly.Client.Scripting/ScriptAPI.json",
                "Anomaly.Client.Scripting/Anomaly.Client.Api.d.lua",
                "tools/MoonSharpHardening/README.md",
                "tools/MoonSharpHardening/Fork-README.md",
                "tools/MoonSharpHardening/LICENSE",
                "samples/HelloPlayerPoses/HelloPlayerPoses.csproj",
                "samples/HelloPlayerPoses/Plugin.cs",
                "samples/HelloPlayerPoses/README.md",
                "samples/Anomaly.Sample.props",
                "samples/HelloAnomalyMod/HelloAnomalyMod.csproj",
                "samples/HelloAnomalyMod/Core.cs",
                "samples/HelloAnomalyMod/README.md",
                "samples/HelloAnomalyModule/HelloAnomalyModule.csproj",
                "samples/HelloAnomalyModule/HelloModule.cs",
                "samples/HelloAnomalyModule/HelloAnomalyModule.dll.anomaly.json.template",
                "samples/HelloAnomalyModule/README.md",
                "lib/net48/BouncyCastle.Cryptography.dll",
                "reference/Anomaly.Client.Api.PublicAPI.Shipped.txt",
                "reference/Anomaly.Server.PublicAPI.Shipped.txt",
                "reference/Anomaly.Client.Scripting.ScriptAPI.Shipped.txt",
                "reference/Anomaly.Client.Scripting.ScriptAPI.json",
                "reference/Anomaly.Client.Api.d.lua",
                "reference/sdk-reference-metadata.json",
                "THIRD_PARTY_DEPENDENCIES.md",
                "LICENSE",
                "NOTICE",
            ]),
        };

        var packageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in layout.Packages)
        {
            if (!packageIds.Add(package.PackageId) || !required.TryGetValue(package.PackageId, out var contract) ||
                !string.Equals(package.Target, contract.Target, StringComparison.Ordinal) ||
                !package.ArchiveName.Contains("{version}", StringComparison.Ordinal) || package.Entries.Count == 0)
                throw new InvalidDataException($"Package layout identity is invalid: {package.PackageId}/{package.Target}.");

            var paths = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in package.Entries)
            {
                ValidateRelativePath(entry.Source, "package source");
                ValidateRelativePath(entry.Path, "package path");
                if (entry.AssemblyVersion is not null)
                {
                    if (!entry.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Assembly version pin requires a DLL entry in '{package.PackageId}'.");
                    ValidateAssemblyVersion(entry.AssemblyVersion, $"package entry '{entry.Path}'");
                }
                var normalizedPath = NormalizeRelativePath(entry.Path);
                if (!paths.Add(normalizedPath) ||
                    (!names.Add(Path.GetFileName(normalizedPath)) &&
                     (package.Target != "developer-sdk" || normalizedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidDataException($"Package '{package.PackageId}' has duplicate paths or filenames.");
            }
            if (contract.Paths.Any(path => !paths.Contains(path)))
                throw new InvalidDataException($"Package '{package.PackageId}' is missing a required runtime or SDK entry.");

            var excludedDependencies = package.PackageId switch
            {
                "anomaly-client" => new[]
                {
                    "0Harmony.dll", "Newtonsoft.Json.dll",
                    "BigGustave.dll", "NVorbis.dll",
                    "Mono.Cecil.dll", "Mono.Cecil.Mdb.dll", "Mono.Cecil.Pdb.dll", "Mono.Cecil.Rocks.dll",
                    "MonoMod.Backports.dll", "MonoMod.Core.dll", "MonoMod.Iced.dll",
                    "MonoMod.ILHelpers.dll", "MonoMod.RuntimeDetour.dll", "MonoMod.Utils.dll",
                },
                "anomaly-server" => new[]
                {
                    "System.Numerics.Vectors.dll", "System.Threading.Tasks.Extensions.dll",
                },
                _ => Array.Empty<string>(),
            };
            if (excludedDependencies.Any(name => names.Contains(name, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Package '{package.PackageId}' includes an excluded dependency.");

            var isRuntimePackage = package.PackageId is "anomaly-client" or "anomaly-server";
            if (isRuntimePackage != (package.RuntimeClosure is not null))
                throw new InvalidDataException(
                    $"Package '{package.PackageId}' has an invalid runtime-closure declaration.");
            if (package.RuntimeClosure is not null)
                ValidateAssemblyClosureDefinition(package.PackageId, package.RuntimeClosure, "runtime");
        }
    }

    private static void ValidateAssemblyClosureDefinition(
        string packageId,
        RuntimeClosureDefinition closure,
        string closureKind)
    {
        if (string.IsNullOrWhiteSpace(closure.RuntimeFramework) || closure.RuntimeFramework.Length > 256)
            throw new InvalidDataException($"{closureKind} framework is invalid for '{packageId}'.");
        if (closure.ReferenceProjects.Count == 0 ||
            closure.ReferenceProjects.Distinct(StringComparer.Ordinal).Count() != closure.ReferenceProjects.Count ||
            closure.ReferenceProjects.Any(project => !ReferenceProjects.Contains(project, StringComparer.Ordinal)))
        {
            throw new InvalidDataException($"Reference-project closure is invalid for '{packageId}'.");
        }

        var frameworkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in closure.FrameworkProvidedAssemblies)
        {
            ValidateAssemblyName(name, $"framework-provided assembly for '{packageId}'");
            if (!frameworkNames.Add(name))
                throw new InvalidDataException($"Duplicate framework-provided assembly '{name}' in '{packageId}'.");
        }

        var hostNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in closure.HostProvidedAssemblies)
        {
            ValidateAssemblyName(item.Name, $"host-provided assembly for '{packageId}'");
            if (string.IsNullOrWhiteSpace(item.Provider) || item.Provider.Length > 256)
                throw new InvalidDataException($"Host provider is invalid for '{item.Name}' in '{packageId}'.");
            if (item.AssemblyVersion is not null)
                ValidateAssemblyVersion(item.AssemblyVersion, $"host-provided '{item.Name}' in '{packageId}'");
            if (item.FileVersion is not null)
                ValidateFileVersion(item.FileVersion, $"host-provided '{item.Name}' in '{packageId}'");
            if (!hostNames.Add(item.Name) || frameworkNames.Contains(item.Name))
                throw new InvalidDataException($"Duplicate or ambiguous provided assembly '{item.Name}' in '{packageId}'.");
        }

        if (frameworkNames.Count == 0 || hostNames.Count == 0)
            throw new InvalidDataException($"{closureKind} closure providers are incomplete for '{packageId}'.");
    }

    private static void ValidateAssemblyName(string name, string label)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256 ||
            name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new InvalidDataException($"Invalid {label}: {name}");
    }

    private static void ValidateAssemblyVersion(string version, string label)
    {
        if (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision < 0 ||
            !string.Equals(parsed.ToString(), version, StringComparison.Ordinal))
            throw new InvalidDataException($"Invalid assembly version for {label}: {version}");
    }

    private static void ValidateFileVersion(string version, string label)
    {
        if (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision < 0 ||
            !string.Equals(parsed.ToString(), version, StringComparison.Ordinal))
            throw new InvalidDataException($"Invalid file version for {label}: {version}");
    }

    private static void CreateArchive(
        string archivePath,
        string payloadRoot,
        IReadOnlyList<PackageInventoryEntry> entries,
        DateTimeOffset timestamp)
    {
        using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        foreach (var item in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            var entry = archive.CreateEntry(item.Path, CompressionLevel.Optimal);
            entry.LastWriteTime = timestamp;
            entry.ExternalAttributes = 0;
            using var input = File.OpenRead(ResolveUnderRoot(payloadRoot, item.Path));
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }

    private static void VerifyArchive(
        string archivePath,
        IReadOnlyDictionary<string, PackageInventoryEntry> expectedEntries,
        DateTimeOffset timestamp)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count != expectedEntries.Count)
            throw new InvalidDataException($"Archive '{Path.GetFileName(archivePath)}' has an unexpected entry count.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            var path = NormalizeRelativePath(entry.FullName);
            ValidateRelativePath(path, "archive entry");
            if (!seen.Add(path) || !expectedEntries.TryGetValue(path, out var expected) ||
                entry.Length != expected.SizeBytes ||
                entry.LastWriteTime.DateTime != timestamp.DateTime)
                throw new InvalidDataException($"Archive entry metadata is invalid: {entry.FullName}.");
            using var stream = entry.Open();
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(hash, expected.Sha256, StringComparison.Ordinal))
                throw new CryptographicException($"Archive entry hash mismatch: {entry.FullName}.");
        }
    }

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        ValidateRelativePath(relativePath, "relative path");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes its root: {relativePath}");
        return fullPath;
    }

    private static void ValidateRelativePath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') ||
            path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(part => part is "." or ".."))
            throw new InvalidDataException($"Invalid {label}: {path}");
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static void ValidateRegularFile(string path, string label)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{label} is missing.", path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{label} must not be a reparse point: {path}");
        if (new FileInfo(path).Length <= 0)
            throw new InvalidDataException($"{label} is empty: {path}");
    }

    private static void ValidateSigningEvidence(
        string output,
        DependencyInventory inventory,
        ReceiptSigning signing)
    {
        if (signing is null)
            throw new InvalidDataException("Release receipt signing state is missing.");

        if (string.Equals(signing.State, "unsigned", StringComparison.Ordinal))
        {
            if (signing.KeyId is not null || signing.TrustedKeyIds.Count != 0 || signing.Files.Count != 0)
                throw new InvalidDataException(
                    "Unsigned release evidence must have no keyId, trustedKeyIds, or signing files.");
            return;
        }

        if (!string.Equals(signing.State, "signed", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(signing.KeyId))
            throw new InvalidDataException("Release receipt signing state must be 'unsigned' or 'signed'.");
        ValidateKeyId(signing.KeyId);
        if (signing.TrustedKeyIds.Count != 1 ||
            !string.Equals(signing.TrustedKeyIds[0], signing.KeyId, StringComparison.Ordinal))
            throw new InvalidDataException("Signed release evidence must identify exactly the verified signing keyId.");

        var expectedPaths = GetExpectedSigningPaths(inventory);
        ValidateSignedManifestSet(output, inventory, signing.KeyId, expectedPaths);
        ValidateRecordedFiles(output, signing.Files, expectedPaths, "Signing evidence");
    }

    private static void ValidateSignedManifestSet(
        string output,
        DependencyInventory inventory,
        string keyId,
        IReadOnlyList<string> expectedPaths,
        ECDsa? verifier = null)
    {
        var manifestRoot = ResolveUnderRoot(output, "manifests");
        if (!Directory.Exists(manifestRoot))
            throw new DirectoryNotFoundException($"Signed manifest directory not found: {manifestRoot}");
        RejectReparsePoints(manifestRoot, "Signed manifest");
        var actualPaths = Directory.EnumerateFiles(manifestRoot, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(output, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        if (!actualPaths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
            throw new InvalidDataException("Signed manifest directory contains missing or unexpected files.");

        foreach (var package in inventory.Packages)
        {
            var manifestPath = ResolveUnderRoot(output, $"manifests/{package.PackageId}.json");
            ValidateRegularFile(manifestPath, $"Signed manifest for '{package.PackageId}'");
            ValidateRegularFile(manifestPath + ".sig", $"Manifest signature for '{package.PackageId}'");
            var manifestBytes = File.ReadAllBytes(manifestPath);
            if (verifier is not null)
            {
                var signature = Convert.FromBase64String(File.ReadAllText(manifestPath + ".sig").Trim());
                if (!verifier.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256))
                    throw new CryptographicException($"Release manifest signature is invalid for '{package.PackageId}'.");
            }
            using var document = JsonDocument.Parse(manifestBytes);
            var root = document.RootElement;
            if (!root.TryGetProperty("keyId", out var manifestKeyId) ||
                !string.Equals(manifestKeyId.GetString(), keyId, StringComparison.Ordinal) ||
                !root.TryGetProperty("packageId", out var manifestPackageId) ||
                !string.Equals(manifestPackageId.GetString(), package.PackageId, StringComparison.Ordinal) ||
                !root.TryGetProperty("target", out var manifestTarget) ||
                !string.Equals(manifestTarget.GetString(), package.Target, StringComparison.Ordinal) ||
                !root.TryGetProperty("sourceCommit", out var manifestSourceCommit) ||
                !string.Equals(manifestSourceCommit.GetString(), inventory.SourceCommit, StringComparison.Ordinal) ||
                !root.TryGetProperty("version", out var manifestVersion) ||
                !string.Equals(manifestVersion.GetString(), inventory.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Signed manifest identity mismatch for '{package.PackageId}'.");
            }
        }

        ValidateRegularFile(
            ResolveUnderRoot(output, "manifests/anomaly-client-official-envelope.json"),
            "Official client release envelope");
    }

    private static List<string> GetExpectedSigningPaths(DependencyInventory inventory)
    {
        var paths = inventory.Packages.SelectMany(package => new[]
            {
                $"manifests/{package.PackageId}.json",
                $"manifests/{package.PackageId}.json.sig",
            })
            .Append("manifests/anomaly-client-official-envelope.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        if (paths.Count != paths.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidDataException("Signed manifest paths are not unique.");
        return paths;
    }

    private static void ValidateGateEvidence(
        string output,
        ReceiptGateEvidence evidence,
        bool requireRecordedFiles)
    {
        if (evidence.SchemaVersion != 1 || evidence.CompletedUtc == default)
            throw new InvalidDataException("Complete-gate evidence has an unsupported schema or completion time.");
        ValidateRelativePath(evidence.SummaryPath, "gate evidence summary path");
        if (!string.Equals(evidence.SummaryPath, "evidence/gate-summary.json", StringComparison.Ordinal))
            throw new InvalidDataException("Complete-gate evidence has an unexpected summary path.");

        var requiredTools = new[] { "dotnet-runtime", "dotnet-sdk", "git", "powershell", "ripgrep" };
        var toolNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in evidence.ToolVersions)
        {
            if (string.IsNullOrWhiteSpace(tool.Name) || string.IsNullOrWhiteSpace(tool.Version) ||
                !toolNames.Add(tool.Name))
                throw new InvalidDataException("Complete-gate tool versions contain an empty or duplicate entry.");
        }
        if (requiredTools.Any(tool => !toolNames.Contains(tool)))
            throw new InvalidDataException("Complete-gate evidence is missing a required tool/runtime version.");

        var requiredGates = new[]
        {
            "repository-secret-scan",
            "compile-list-verification",
            "release-build",
            "core-tests",
            "public-api-baseline",
            "release-tool-tests",
            "primary-package-build",
            "package-closure",
            "sdk-compile-closure",
            "public-api-fixtures",
            "reproducibility-rebuild",
            "reproducibility-package-build",
            "reproducibility-comparison",
        };
        var gateNames = new HashSet<string>(StringComparer.Ordinal);
        var resultCount = 0;
        foreach (var gate in evidence.Gates)
        {
            if (string.IsNullOrWhiteSpace(gate.Name) || !gateNames.Add(gate.Name) ||
                !string.Equals(gate.Status, "passed", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(gate.Command) || gate.StartedUtc == default ||
                gate.CompletedUtc < gate.StartedUtc || gate.CompletedUtc > evidence.CompletedUtc)
                throw new InvalidDataException("Complete-gate evidence contains an invalid or failed gate result.");
            ValidateRelativePath(gate.LogPath, $"log path for gate '{gate.Name}'");
            if (!gate.LogPath.StartsWith("evidence/logs/", StringComparison.Ordinal) ||
                !gate.LogPath.EndsWith(".log", StringComparison.Ordinal))
                throw new InvalidDataException($"Gate '{gate.Name}' has an invalid log path.");
            foreach (var resultPath in gate.ResultPaths)
            {
                ValidateRelativePath(resultPath, $"result path for gate '{gate.Name}'");
                if (!resultPath.StartsWith("evidence/test-results/", StringComparison.Ordinal) ||
                    !resultPath.EndsWith(".trx", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Gate '{gate.Name}' has an unsupported result path.");
                resultCount++;
            }
        }
        if (requiredGates.Any(gate => !gateNames.Contains(gate)) || resultCount == 0)
            throw new InvalidDataException("Complete-gate evidence is missing a required pass state or TRX result.");

        if (!requireRecordedFiles)
        {
            if (evidence.Files.Count != 0)
                throw new InvalidDataException("Unfinalized gate evidence must not predeclare file hashes.");
            return;
        }

        var expectedPaths = GetExpectedGateEvidencePaths(evidence);
        ValidateRecordedFiles(output, evidence.Files, expectedPaths, "Complete-gate evidence");
        var evidenceRoot = ResolveUnderRoot(output, "evidence");
        RejectReparsePoints(evidenceRoot, "Complete-gate evidence");
        var actualPaths = Directory.EnumerateFiles(evidenceRoot, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(output, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        if (!actualPaths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
            throw new InvalidDataException("Complete-gate evidence contains missing or unexpected files.");
    }

    private static List<string> GetExpectedGateEvidencePaths(ReceiptGateEvidence evidence)
    {
        var paths = evidence.Gates.Select(gate => gate.LogPath)
            .Concat(evidence.Gates.SelectMany(gate => gate.ResultPaths))
            .Append(evidence.SummaryPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        if (paths.Count != paths.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidDataException("Complete-gate evidence paths are not unique.");
        return paths;
    }

    private static void ValidateRecordedFiles(
        string output,
        IReadOnlyList<ReceiptEvidenceFile> files,
        IReadOnlyList<string> expectedPaths,
        string label)
    {
        var recordedPaths = files.Select(file => file.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        if (!recordedPaths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
            throw new InvalidDataException($"{label} file inventory is incomplete.");
        foreach (var file in files)
        {
            ValidateRelativePath(file.Path, $"{label} file path");
            var fullPath = ResolveUnderRoot(output, file.Path);
            ValidateRegularFile(fullPath, $"{label} file '{file.Path}'");
            var info = new FileInfo(fullPath);
            if (info.Length != file.SizeBytes ||
                !string.Equals(HashFile(fullPath), file.Sha256, StringComparison.Ordinal))
                throw new CryptographicException($"{label} file '{file.Path}' does not match its receipt.");
        }
    }

    private static void RejectReparsePoints(string root, string label)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 ||
            Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidDataException($"{label} directories must not be reparse points.");
    }

    private static void ValidateKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > 128 ||
            keyId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
            throw new InvalidDataException("Signing keyId is invalid.");
    }

    private static void EnsureP256(ECDsa key, string label)
    {
        const string NistP256Oid = "1.2.840.10045.3.1.7";
        var parameters = key.ExportParameters(false);
        if (key.KeySize != 256 ||
            !string.Equals(parameters.Curve.Oid.Value, NistP256Oid, StringComparison.Ordinal))
            throw new CryptographicException($"{label} must be ECDSA P-256.");
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 64 ||
            version.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+' or '_')))
            throw new InvalidDataException("Release version is invalid.");
    }

    private static void ValidateSourceCommit(string sourceCommit)
    {
        if (sourceCommit.Length is not (40 or 64) ||
            sourceCommit.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException("Source commit must be a lowercase Git object id.");
    }

    private static DateTimeOffset ClampZipTimestamp(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var minimum = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var maximum = new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero);
        if (utc < minimum)
            return minimum;
        if (utc > maximum)
            return maximum;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second / 2 * 2, TimeSpan.Zero);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void WriteAtomically(string path, ReadOnlySpan<byte> bytes)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private sealed record ManagedAssemblyReference(string Name, string Version);

    private sealed record ManagedAssemblyMetadata(
        string Name,
        string Version,
        string Path,
        long SizeBytes,
        string Sha256,
        IReadOnlyList<ManagedAssemblyReference> References);
}
