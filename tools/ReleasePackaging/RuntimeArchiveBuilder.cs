using Anomaly.AssetValidation;
using Anomaly.Shared.Assets;
using System.Text;
using System.Text.Json;

namespace Anomaly.ReleasePackaging;

internal static partial class ReleasePackageBuilder
{
    private const string SkinPackDirectory = "UserData/Anomaly/AssetBundles";

    internal static void CreateLocalReleaseArchives(
        string repositoryRoot,
        string outputDirectory,
        string version,
        string referenceDirectory,
        string skinIndexPath,
        bool includeSdk = true)
    {
        ValidateVersion(version);
        var repository = Path.GetFullPath(repositoryRoot);
        var output = Path.GetFullPath(outputDirectory);
        var references = Path.GetFullPath(referenceDirectory);
        if (File.Exists(output) ||
            (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()))
            throw new IOException($"Runtime output directory must be absent or empty: {output}");

        var layout = JsonSerializer.Deserialize(
            File.ReadAllBytes(ResolveUnderRoot(repository, LayoutRelativePath)),
            ReleaseJsonContext.Default.PackageLayout)
            ?? throw new InvalidDataException("Package layout is empty.");
        ValidateLayout(layout);
        var inputs = ReadResolvedInputs(references);
        var packages = layout.Packages
            .Where(package => includeSdk || package.Target is "client-runtime" or "server-runtime")
            .OrderBy(package => package.PackageId, StringComparer.Ordinal)
            .ToArray();
        var payloadRoot = Path.Combine(references, "payloads");
        var archiveRoot = Path.Combine(references, "local-archives");
        if (Directory.Exists(payloadRoot) || Directory.Exists(archiveRoot))
            throw new IOException("Runtime staging directories already exist; use a fresh reference directory.");
        Directory.CreateDirectory(archiveRoot);
        var timestamp = ClampZipTimestamp(DateTimeOffset.UtcNow);

        // Stage, audit, and verify every archive before copying it to the output.
        foreach (var package in packages)
        {
            var packageRoot = Path.Combine(payloadRoot, package.Target);
            var entries = new Dictionary<string, PackageInventoryEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in package.Entries)
            {
                // The selected skin pair is staged below; all other declared files include their notices.
                if (package.Target == "client-runtime" &&
                    definition.Path.StartsWith(SkinPackDirectory + "/", StringComparison.Ordinal))
                    continue;
                StageRuntimeFile(
                    ResolveUnderRoot(repository, definition.Source), definition.Path, packageRoot, entries);
            }

            if (package.Target == "client-runtime")
            {
                var sourceIndex = Path.GetFullPath(skinIndexPath);
                var index = ReadSkinIndex(sourceIndex);
                StageRuntimeFile(sourceIndex,
                    SkinPackDirectory + "/" + Path.GetFileName(sourceIndex), packageRoot, entries);
                StageRuntimeFile(Path.Combine(Path.GetDirectoryName(sourceIndex)!, index.BundleFile),
                    SkinPackDirectory + "/" + index.BundleFile, packageRoot, entries);
                ValidateSkinPack(ResolveUnderRoot(packageRoot,
                    SkinPackDirectory + "/" + Path.GetFileName(sourceIndex)));
            }

            var closure = BuildAssemblyClosure(package, packageRoot, inputs, package.RuntimeClosure, "runtime");
            if (closure is not null)
                Console.WriteLine($"{package.PackageId}: {closure.PackagedAssemblies.Count} runtime DLLs; " +
                                  "all assembly references resolve to packaged, framework, or observed host assemblies.");
            else
                Console.WriteLine($"{package.PackageId}: {entries.Count} SDK libraries, documentation, and starter files.");
            var archiveName = package.ArchiveName.Replace("{version}", version, StringComparison.Ordinal);
            var archivePath = ResolveUnderRoot(archiveRoot, archiveName);
            CreateArchive(archivePath, packageRoot, entries.Values.ToArray(), timestamp);
            VerifyArchive(archivePath, entries, timestamp);
        }

        Directory.CreateDirectory(output);
        var checksums = new List<string>();
        foreach (var package in packages)
        {
            var archiveName = package.ArchiveName.Replace("{version}", version, StringComparison.Ordinal);
            var destination = ResolveUnderRoot(output, archiveName);
            File.Copy(ResolveUnderRoot(archiveRoot, archiveName), destination, overwrite: false);
            var hash = HashFile(destination);
            checksums.Add($"{hash}  {archiveName}");
            Console.WriteLine($"Created {destination} (SHA-256 {hash}).");
        }
        if (includeSdk)
            WriteAtomically(Path.Combine(output, "SHA256SUMS"), Encoding.UTF8.GetBytes(string.Join("\n", checksums) + "\n"));
    }

    internal static void ValidateSkinPack(string skinIndexPath)
    {
        var indexPath = Path.GetFullPath(skinIndexPath);
        var index = ReadSkinIndex(indexPath);
        var bundlePath = Path.Combine(Path.GetDirectoryName(indexPath)!, index.BundleFile);
        ValidateRegularFile(bundlePath, "Skin bundle");
        if (new FileInfo(bundlePath).Length != index.BundleBytes)
            throw new InvalidDataException("Skin bundle length does not match its index.");
        using var stream = File.OpenRead(bundlePath);
        using var validated = BundleValidator.Validate(stream, new BundleValidationRequest(
            index.BundleSha256,
            index.Assets.Select(asset => new BundleAssetExpectation(
                asset.Address, asset.EntryName, classId: 28, width: asset.Width, height: asset.Height))));
        Console.WriteLine($"Validated logo/background skin pack '{index.PackId}'.");
    }

    private static AssetPackIndex ReadSkinIndex(string indexPath)
    {
        ValidateRegularFile(indexPath, "Skin index");
        if (!indexPath.EndsWith(".index.json", StringComparison.OrdinalIgnoreCase) ||
            new FileInfo(indexPath).Length > AssetPackIndexCodec.MaxIndexBytes)
            throw new InvalidDataException("Skin index must be a bounded *.index.json file.");
        var index = AssetPackIndexCodec.Parse(File.ReadAllBytes(indexPath), remote: false);
        var required = new HashSet<string>(StringComparer.Ordinal)
        {
            "game/skin/logo", "game/skin/background",
        };
        if (index.Visuals.Count != 0 || index.Assets.Count != required.Count ||
            !required.SetEquals(index.Assets.Select(asset => asset.Address)) ||
            index.Assets.Any(asset => asset.AssetKind != AssetKind.Texture2D))
            throw new InvalidDataException("Skin pack must contain exactly the logo and background Texture2D roots.");
        return index;
    }

    private static void StageRuntimeFile(
        string source,
        string path,
        string packageRoot,
        IDictionary<string, PackageInventoryEntry> entries)
    {
        ValidateRegularFile(source, $"Runtime source '{source}'");
        var normalized = NormalizeRelativePath(path);
        if (entries.ContainsKey(normalized))
            throw new InvalidDataException($"Duplicate runtime package entry: {normalized}");
        var destination = ResolveUnderRoot(packageRoot, normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
        entries.Add(normalized, new PackageInventoryEntry
        {
            Path = normalized,
            Name = Path.GetFileName(normalized),
            SizeBytes = new FileInfo(destination).Length,
            Sha256 = HashFile(destination),
        });
    }
}
