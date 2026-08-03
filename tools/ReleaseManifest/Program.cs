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
    if (args.Length < 8)
        throw new ArgumentException("create requires: <release-id> <version> <source-commit> <dependency-inventory> <output.json> <private-key.pem> <artifact> [...]");

    var inventoryPath = Path.GetFullPath(args[4]);
    if (!File.Exists(inventoryPath)) throw new FileNotFoundException("Dependency inventory not found.", inventoryPath);
    var outputPath = Path.GetFullPath(args[5]);
    var keyPath = Path.GetFullPath(args[6]);
    var artifacts = args.Skip(7)
        .Select(Path.GetFullPath)
        .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
        .Select(path =>
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Artifact not found.", path);
            using var stream = File.OpenRead(path);
            return new Artifact
            {
                Name = Path.GetFileName(path),
                SizeBytes = stream.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
            };
        }).ToList();

    if (artifacts.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != artifacts.Count)
        throw new InvalidOperationException("Artifact filenames must be unique.");

    var manifest = new Manifest
    {
        ReleaseId = args[1],
        Version = args[2],
        SourceCommit = args[3].ToLowerInvariant(),
        DependencyInventorySha256 = HashFile(inventoryPath),
        GeneratedUtc = DateTimeOffset.UtcNow,
        Artifacts = artifacts,
    };
    Validate(manifest);

    var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Serialization.Options);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllBytes(outputPath, bytes);

    using var signer = ECDsa.Create();
    signer.ImportFromPem(File.ReadAllText(keyPath));
    var signature = signer.SignData(bytes, HashAlgorithmName.SHA256);
    File.WriteAllText(outputPath + ".sig", Convert.ToBase64String(signature));
    Console.WriteLine($"Created {outputPath} with {artifacts.Count} artifact(s).");
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
    var signature = Convert.FromBase64String(File.ReadAllText(signaturePath).Trim());
    if (!verifier.VerifyData(bytes, signature, HashAlgorithmName.SHA256))
        throw new CryptographicException("Release manifest signature is invalid.");

    if (args.Length == 6)
    {
        var artifactDirectory = Path.GetFullPath(args[4]);
        var inventoryPath = Path.GetFullPath(args[5]);
        if (!string.Equals(HashFile(inventoryPath), manifest.DependencyInventorySha256,
                StringComparison.Ordinal))
            throw new CryptographicException("Dependency inventory hash does not match the signed manifest.");
        foreach (var artifact in manifest.Artifacts)
        {
            var artifactPath = Path.Combine(artifactDirectory, artifact.Name);
            if (!File.Exists(artifactPath))
                throw new FileNotFoundException("Signed release artifact is missing.", artifactPath);
            var info = new FileInfo(artifactPath);
            if (info.Length != artifact.SizeBytes ||
                !string.Equals(HashFile(artifactPath), artifact.Sha256, StringComparison.Ordinal))
                throw new CryptographicException($"Artifact '{artifact.Name}' does not match the signed manifest.");
        }
    }
    Console.WriteLine($"Verified {manifest.ReleaseId} {manifest.Version} ({manifest.Artifacts.Count} artifact(s)).");
    return 0;
}

static int GenerateKey(string[] args)
{
    if (args.Length != 3)
        throw new ArgumentException("generate-key requires: <private-key.pem> <public-key.pem>");
    var privatePath = Path.GetFullPath(args[1]);
    var publicPath = Path.GetFullPath(args[2]);
    if (File.Exists(privatePath) || File.Exists(publicPath))
        throw new IOException("Refusing to overwrite an existing signing key.");
    Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(privatePath, key.ExportECPrivateKeyPem());
    File.WriteAllText(publicPath, key.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine("Generated an ECDSA P-256 release-signing keypair.");
    return 0;
}

static void Validate(Manifest manifest)
{
    if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported manifest schema.");
    if (string.IsNullOrWhiteSpace(manifest.ReleaseId) || manifest.ReleaseId.Length > 128)
        throw new InvalidDataException("ReleaseId is invalid.");
    if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 64)
        throw new InvalidDataException("Version is invalid.");
    if (!IsLowerHex(manifest.SourceCommit, 40) && !IsLowerHex(manifest.SourceCommit, 64))
        throw new InvalidDataException("SourceCommit must be a lowercase Git object id.");
    if (!IsLowerHex(manifest.DependencyInventorySha256, 64))
        throw new InvalidDataException("DependencyInventorySha256 is invalid.");
    if (manifest.Artifacts.Count is < 1 or > 256)
        throw new InvalidDataException("Artifact count is invalid.");
    foreach (var artifact in manifest.Artifacts)
    {
        if (string.IsNullOrWhiteSpace(artifact.Name) || artifact.Name != Path.GetFileName(artifact.Name))
            throw new InvalidDataException("Artifact names must be bare filenames.");
        if (artifact.SizeBytes <= 0 || artifact.Sha256.Length != 64 ||
            artifact.Sha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"Artifact '{artifact.Name}' metadata is invalid.");
    }
}

static bool IsLowerHex(string value, int length) =>
    value is not null && value.Length == length &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

static string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  create <release-id> <version> <source-commit> <dependency-inventory> <output.json> <private-key.pem> <artifact> [...]");
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
    };
}

sealed class Manifest
{
    public int SchemaVersion { get; set; } = 1;
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
