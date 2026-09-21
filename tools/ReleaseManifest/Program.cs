using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length < 1)
{
    Usage();
    return 2;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "create" => Create(args),
        "verify" => Verify(args),
        "generate-key" => GenerateKey(args),
        _ => Usage(),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Create(string[] args)
{
    if (args.Length < 11)
    {
        throw new ArgumentException(
            "create requires: <key-id> <target> <package-id> <release-id> <version> <source-commit> <dependency-inventory> <output.json> <private-key.pem> <artifact> [...]");
    }

    var inventoryPath = Path.GetFullPath(args[7]);
    if (!File.Exists(inventoryPath))
        throw new FileNotFoundException("Dependency inventory not found.", inventoryPath);
    var outputPath = Path.GetFullPath(args[8]);
    var keyPath = Path.GetFullPath(args[9]);
    if (!File.Exists(keyPath))
        throw new FileNotFoundException("Release signing key not found.", keyPath);

    var artifacts = args.Skip(10)
        .Select(Path.GetFullPath)
        .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
        .Select(path =>
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Artifact not found.", path);
            using var stream = File.OpenRead(path);
            return new Artifact
            {
                Name = Path.GetFileName(path),
                SizeBytes = stream.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
            };
        }).ToList();

    if (artifacts.Select(artifact => artifact.Name).Distinct(StringComparer.Ordinal).Count() != artifacts.Count)
        throw new InvalidOperationException("Artifact filenames must be unique.");

    var manifest = new Manifest
    {
        KeyId = args[1],
        Target = args[2],
        PackageId = args[3],
        ReleaseId = args[4],
        Version = args[5],
        SourceCommit = args[6].ToLowerInvariant(),
        DependencyInventorySha256 = HashFile(inventoryPath),
        GeneratedUtc = GetGeneratedUtc(),
        Artifacts = artifacts,
    };
    Validate(manifest);

    var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Serialization.Options);
    using var signer = ECDsa.Create();
    signer.ImportFromPem(File.ReadAllText(keyPath));
    EnsureP256(signer, "Release signing key");
    var signature = signer.SignData(bytes, HashAlgorithmName.SHA256);

    WriteAtomically(outputPath, bytes);
    WriteAtomically(outputPath + ".sig", System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
    Console.WriteLine($"Created schema-v2 manifest {outputPath} with {artifacts.Count} artifact(s).");
    return 0;
}

static int Verify(string[] args)
{
    if (args.Length is not 4 and not 6)
        throw new ArgumentException("verify requires: <manifest.json> <signature> <public-key.pem> [artifact-directory dependency-inventory]");

    var manifestPath = Path.GetFullPath(args[1]);
    var signaturePath = Path.GetFullPath(args[2]);
    var keyPath = Path.GetFullPath(args[3]);
    var bytes = File.ReadAllBytes(manifestPath);
    var manifest = JsonSerializer.Deserialize<Manifest>(bytes, Serialization.Options)
                   ?? throw new InvalidDataException("Manifest is empty.");
    Validate(manifest);

    using var verifier = ECDsa.Create();
    verifier.ImportFromPem(File.ReadAllText(keyPath));
    EnsureP256(verifier, "Release verification key");
    var signature = Convert.FromBase64String(File.ReadAllText(signaturePath).Trim());
    if (!verifier.VerifyData(bytes, signature, HashAlgorithmName.SHA256))
        throw new CryptographicException("Release manifest signature is invalid.");

    if (args.Length == 6)
    {
        var artifactDirectory = Path.GetFullPath(args[4]);
        var inventoryPath = Path.GetFullPath(args[5]);
        if (!Directory.Exists(artifactDirectory))
            throw new DirectoryNotFoundException($"Artifact directory not found: {artifactDirectory}");
        if (!string.Equals(HashFile(inventoryPath), manifest.DependencyInventorySha256, StringComparison.Ordinal))
            throw new CryptographicException("Dependency inventory hash does not match the signed manifest.");

        var artifactFiles = Directory.EnumerateFiles(artifactDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new { Name = GetRequiredFileName(path), Path = path })
            .GroupBy(file => file.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single().Path
                    : throw new InvalidDataException($"Artifact directory contains duplicate filename '{group.Key}'."),
                StringComparer.Ordinal);
        foreach (var artifact in manifest.Artifacts)
        {
            if (!artifactFiles.TryGetValue(artifact.Name, out var artifactPath))
                throw new FileNotFoundException("Signed release artifact is missing.", artifact.Name);
            var info = new FileInfo(artifactPath);
            if (info.Length != artifact.SizeBytes ||
                !string.Equals(HashFile(artifactPath), artifact.Sha256, StringComparison.Ordinal))
            {
                throw new CryptographicException($"Artifact '{artifact.Name}' does not match the signed manifest.");
            }
        }
    }

    Console.WriteLine(
        $"Verified {manifest.PackageId}/{manifest.Target} {manifest.Version} with key {manifest.KeyId} ({manifest.Artifacts.Count} artifact(s)).");
    return 0;
}

static string GetRequiredFileName(string path)
{
    return Path.GetFileName(path)
           ?? throw new InvalidDataException($"Artifact path does not contain a filename: {path}");
}

static int GenerateKey(string[] args)
{
    if (args.Length != 3)
        throw new ArgumentException("generate-key requires: <private-key.pem> <public-key.pem>");
    var privatePath = Path.GetFullPath(args[1]);
    var publicPath = Path.GetFullPath(args[2]);
    if (File.Exists(privatePath) || File.Exists(publicPath))
        throw new IOException("Refusing to overwrite an existing signing key.");

    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    WriteAtomically(privatePath, System.Text.Encoding.ASCII.GetBytes(key.ExportECPrivateKeyPem()));
    WriteAtomically(publicPath, System.Text.Encoding.ASCII.GetBytes(key.ExportSubjectPublicKeyInfoPem()));
    Console.WriteLine("Generated an ECDSA P-256 release-signing keypair.");
    return 0;
}

static void Validate(Manifest manifest)
{
    if (manifest.SchemaVersion != 2)
        throw new InvalidDataException("Unsupported manifest schema; schemaVersion 2 is required.");
    ValidateIdentifier(manifest.KeyId, "keyId", 128);
    ValidateIdentifier(manifest.Target, "target", 64);
    ValidateIdentifier(manifest.PackageId, "packageId", 128);
    ValidateIdentifier(manifest.ReleaseId, "releaseId", 128);
    if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 64)
        throw new InvalidDataException("version is invalid.");
    if (!IsLowerHex(manifest.SourceCommit, 40) && !IsLowerHex(manifest.SourceCommit, 64))
        throw new InvalidDataException("sourceCommit must be a lowercase Git object id.");
    if (!IsLowerHex(manifest.DependencyInventorySha256, 64))
        throw new InvalidDataException("dependencyInventorySha256 is invalid.");
    if (manifest.GeneratedUtc == default || manifest.GeneratedUtc.Offset != TimeSpan.Zero)
        throw new InvalidDataException("generatedUtc must be a non-default UTC timestamp.");
    if (manifest.Artifacts.Count is < 1 or > 256)
        throw new InvalidDataException("Artifact count is invalid.");

    var names = new HashSet<string>(StringComparer.Ordinal);
    string? previousName = null;
    foreach (var artifact in manifest.Artifacts)
    {
        if (string.IsNullOrWhiteSpace(artifact.Name) || artifact.Name != Path.GetFileName(artifact.Name) ||
            artifact.Name.Contains('/') || artifact.Name.Contains('\\') || !names.Add(artifact.Name))
        {
            throw new InvalidDataException("Artifact names must be unique bare filenames.");
        }
        if (previousName is not null && StringComparer.Ordinal.Compare(previousName, artifact.Name) >= 0)
            throw new InvalidDataException("Artifacts must use canonical ordinal filename ordering.");
        previousName = artifact.Name;

        if (artifact.SizeBytes <= 0 || !IsLowerHex(artifact.Sha256, 64))
            throw new InvalidDataException($"Artifact '{artifact.Name}' metadata is invalid.");
    }
}

static void ValidateIdentifier(string value, string name, int maximumLength)
{
    if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength ||
        value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
    {
        throw new InvalidDataException($"{name} is invalid.");
    }
}

static DateTimeOffset GetGeneratedUtc()
{
    var sourceDateEpoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
    if (string.IsNullOrWhiteSpace(sourceDateEpoch))
        return DateTimeOffset.UtcNow;
    if (!long.TryParse(sourceDateEpoch, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds < 0)
        throw new InvalidDataException("SOURCE_DATE_EPOCH must be a non-negative Unix timestamp.");
    return DateTimeOffset.FromUnixTimeSeconds(seconds);
}

static void EnsureP256(ECDsa key, string label)
{
    const string NistP256Oid = "1.2.840.10045.3.1.7";
    var parameters = key.ExportParameters(false);
    if (key.KeySize != 256 ||
        !string.Equals(parameters.Curve.Oid.Value, NistP256Oid, StringComparison.Ordinal))
        throw new CryptographicException($"{label} must be ECDSA P-256.");
}

static bool IsLowerHex(string? value, int length) =>
    value is not null && value.Length == length &&
    value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

static string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static void WriteAtomically(string path, ReadOnlySpan<byte> bytes)
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

static int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  create <key-id> <target> <package-id> <release-id> <version> <source-commit> <dependency-inventory> <output.json> <private-key.pem> <artifact> [...]");
    Console.Error.WriteLine("  verify <manifest.json> <signature> <public-key.pem> [artifact-directory dependency-inventory]");
    Console.Error.WriteLine("  generate-key <private-key.pem> <public-key.pem>");
    return 2;
}

static class Serialization
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}

sealed class Manifest
{
    public int SchemaVersion { get; set; } = 2;
    public string KeyId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string ReleaseId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string SourceCommit { get; set; } = string.Empty;
    public string DependencyInventorySha256 { get; set; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; set; }
    public List<Artifact> Artifacts { get; set; } = new();
}

sealed class Artifact
{
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}
