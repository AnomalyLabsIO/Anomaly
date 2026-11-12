using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anomaly.AssetValidation;
using Anomaly.Shared.Assets;

try
{
    if (args.Length < 2 || args.Length > 4)
        throw new ArgumentException("Usage: AssetPackIndexUpgrade <input.index.json> <new-output.index.json> [--local] [--bundle=<path>]");
    string sourcePath = Path.GetFullPath(args[0]);
    string outputPath = Path.GetFullPath(args[1]);
    bool local = false;
    string bundlePath = null;
    foreach (string option in args.Skip(2))
    {
        if (option == "--local" && !local) local = true;
        else if (option.StartsWith("--bundle=", StringComparison.Ordinal) && bundlePath == null)
            bundlePath = Path.GetFullPath(option.Substring("--bundle=".Length));
        else throw new ArgumentException("Unknown or repeated option: " + option);
    }
    if (File.Exists(outputPath) || Directory.Exists(outputPath) ||
        string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
        throw new IOException("Choose a new output path. Existing indexes are never replaced by this tool.");
    byte[] input;
    using (var source = File.OpenRead(sourcePath))
    {
        if (source.Length <= 0 || source.Length > AssetPackIndexCodec.MaxIndexBytes)
            throw new InvalidDataException("Index exceeds the bounded authoring size.");
        input = new byte[checked((int)source.Length)];
        source.ReadExactly(input);
    }
    using (var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 18 }))
        RejectDuplicateProperties(document.RootElement);
    var json = JsonNode.Parse(input, documentOptions: new JsonDocumentOptions { MaxDepth = 18 }) as JsonObject
        ?? throw new InvalidDataException("Expected an asset index object.");
    int schema = json["SchemaVersion"]?.GetValue<int>() ?? 0;
    if (schema != 1 && schema != AssetPackIndex.CurrentSchemaVersion)
        throw new InvalidDataException("Only asset index schemas 1 and 2 can be explicitly rebuilt.");
    json["SchemaVersion"] = AssetPackIndex.CurrentSchemaVersion;
    json["DecodedBytes"] = 1; // Only a pre-validation placeholder; never published.
    var index = AssetPackIndexCodec.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()), remote: !local);
    bundlePath ??= Path.Combine(Path.GetDirectoryName(sourcePath), index.BundleFile);
    if (string.Equals(bundlePath, outputPath, StringComparison.OrdinalIgnoreCase))
        throw new IOException("The output index cannot replace its bundle.");
    var expectations = index.Assets.Select(asset => new BundleAssetExpectation(asset.Address, asset.EntryName,
        asset.AssetKind switch { AssetKind.Texture2D => 28, AssetKind.Sprite => 213, AssetKind.AudioClip => 83, AssetKind.Mesh => 43, _ => 0 },
        asset.Width, asset.Height, asset.SampleFrames, asset.SampleRate, asset.Channels, asset.VertexCount, asset.TriangleCount));
    long decoded;
    using (var bundle = File.OpenRead(bundlePath))
    {
        if (bundle.Length != index.BundleBytes) throw new InvalidDataException("Bundle length disagrees with the index.");
        using var verified = BundleValidator.Validate(bundle, new BundleValidationRequest(index.BundleSha256, expectations));
        decoded = verified.TotalDecodedBytes;
    }
    json["DecodedBytes"] = decoded;
    byte[] output = Encoding.UTF8.GetBytes(json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    AssetPackIndexCodec.Parse(output, remote: !local);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
    using (var destination = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    { destination.Write(output); destination.Flush(true); }
    Console.WriteLine(JsonSerializer.Serialize(new { schema = AssetPackIndex.CurrentSchemaVersion, decodedBytes = decoded,
        bundleSha256 = index.BundleSha256, bundleBytes = index.BundleBytes, output = outputPath }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static void RejectDuplicateProperties(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate index property: " + property.Name);
            RejectDuplicateProperties(property.Value);
        }
    }
    else if (element.ValueKind == JsonValueKind.Array)
        foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
}
