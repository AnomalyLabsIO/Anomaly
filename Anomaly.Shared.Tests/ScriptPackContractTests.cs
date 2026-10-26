using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ScriptPackContractTests
{
    [Fact]
    public void GeneratedIndexIsCanonicalAndBindsSourcesAndMessageContracts()
    {
        var index = Pack(Source("b.lua", "return 2"), Source("a.lua", ""));
        index.Messages.Add(new ScriptMessageSchema { Name = "State", Direction = AnomalyMessageDirection.ServerToClient,
            Fields = new List<ScriptMessageField> { new ScriptMessageField { Name = "Zed", Type = "Boolean" }, new ScriptMessageField { Name = "Alpha", Type = "String", MaxBytes = 40 } } });
        byte[] canonical = ScriptPackIndexCodec.Encode(index);
        index.Sources.Reverse(); index.Messages[0].Fields.Reverse();
        Assert.Equal(canonical, ScriptPackIndexCodec.Encode(index));
        ScriptPackIndex parsed = ScriptPackIndexCodec.Parse(canonical);
        Assert.Equal("mods/example/panel/a.lua", parsed.Sources[0].Address);
        Assert.Equal("Alpha", parsed.Messages[0].Fields[0].Name);
        Assert.Equal(ScriptSourceValidator.EmptySha256Hex, parsed.Sources[0].SourceSha256);
        Assert.Equal(canonical, ScriptPackIndexCodec.Encode(parsed));
        parsed.Messages[0].Fields[0].MaxBytes++;
        Assert.NotEqual(ScriptSourceValidator.HashHex(canonical), ScriptSourceValidator.HashHex(ScriptPackIndexCodec.Encode(parsed)));
    }

    [Theory]
    [InlineData("SchemaVersion")]
    [InlineData("PackId")]
    [InlineData("ApiVersion")]
    [InlineData("Sources")]
    [InlineData("SourceBytes")]
    [InlineData("SourceSha256")]
    [InlineData("Address")]
    public void AllSourceInventoryPropertiesAreRequiredIncludingZeroByteCounts(string missing)
    {
        JObject json = JObject.Parse(Encoding.UTF8.GetString(ScriptPackIndexCodec.Encode(Pack(Source("main.lua", "")))));
        if (missing.StartsWith("Source", StringComparison.Ordinal) && missing != "Sources" || missing == "Address") ((JObject)json["Sources"][0]).Remove(missing);
        else json.Remove(missing);
        Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.Parse(Encoding.UTF8.GetBytes(json.ToString())));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("api")]
    [InlineData("owner")]
    [InlineData("reserved")]
    [InlineData("duplicate")]
    [InlineData("extension")]
    [InlineData("traversal")]
    [InlineData("uppercase")]
    [InlineData("negative")]
    [InlineData("oversize")]
    [InlineData("hash")]
    [InlineData("empty-hash")]
    public void InvalidSourcePacksFailClosed(string invalid)
    {
        var index = Pack(Source("main.lua", ""));
        switch (invalid)
        {
            case "schema": index.SchemaVersion++; break;
            case "api": index.ApiVersion++; break;
            case "owner": index.Sources[0].Address = "mods/other/main.lua"; break;
            case "reserved": index.PackId = "anomaly:panel"; break;
            case "duplicate": index.Sources.Add(index.Sources[0]); break;
            case "extension": index.Sources[0].Address += "c"; break;
            case "traversal": index.Sources[0].Address = "mods/example/../main.lua"; break;
            case "uppercase": index.Sources[0].Address = "mods/example/Main.lua"; break;
            case "negative": index.Sources[0].SourceBytes = -1; break;
            case "oversize": index.Sources[0].SourceBytes = 65537; break;
            case "hash": index.Sources[0].SourceSha256 = new string('A', 64); break;
            case "empty-hash": index.Sources[0].SourceSha256 = new string('a', 64); break;
        }
        Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.Validate(index));
    }

    [Fact]
    public void SourceCountAndLogicalByteTotalsAreIndependentBounds()
    {
        ScriptPackIndexCodec.Validate(Pack(Enumerable.Range(0, 8).Select(i => Source(i + ".lua", "")).ToArray()));
        Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.Validate(Pack(Enumerable.Range(0, 9).Select(i => Source(i + ".lua", "")).ToArray())));
        var full = Enumerable.Range(0, 4).Select(i => Source(i + ".lua", new string('x', 65536))).ToArray();
        ScriptPackIndexCodec.Validate(Pack(full));
        Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.Validate(Pack(full.Concat(new[] { Source("extra.lua", "x") }).ToArray())));
    }

    [Theory]
    [InlineData("unknown-root")]
    [InlineData("unknown-source")]
    [InlineData("duplicate-key")]
    [InlineData("invalid-utf8")]
    [InlineData("too-large")]
    [InlineData("trailing")]
    public void IndexJsonRejectsAmbiguousOrUnboundedInputs(string invalid)
    {
        byte[] bytes = ScriptPackIndexCodec.Encode(Pack(Source("main.lua", "")));
        string text = Encoding.UTF8.GetString(bytes);
        switch (invalid)
        {
            case "unknown-root": text = text.Insert(1, "\"Callback\":true,"); break;
            case "unknown-source": text = text.Replace("\"SourceBytes\":0", "\"SourceBytes\":0,\"Path\":\"escape\""); break;
            case "duplicate-key": text = text.Insert(1, "\"SchemaVersion\":1,"); break;
            case "invalid-utf8": bytes = new byte[] { 0xff }; break;
            case "too-large": bytes = new byte[ScriptPackLimits.MaxIndexBytes + 1]; break;
            case "trailing": text += "{}"; break;
        }
        if (invalid != "invalid-utf8" && invalid != "too-large") bytes = Encoding.UTF8.GetBytes(text);
        Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.Parse(bytes));
    }

    [Fact]
    public void MessageDescriptorUsesTheSameStrictSchemasAsGeneratedIndexes()
    {
        var schemas = ScriptPackIndexCodec.ParseMessageSchemas(Encoding.UTF8.GetBytes("{\"Messages\":[{\"Name\":\"Click\",\"Direction\":\"ClientToServer\",\"Fields\":[{\"Name\":\"Id\",\"Type\":\"UInt32\"}]}]}"));
        Assert.Equal("Click", Assert.Single(schemas).Name);
        foreach (string invalid in new[] { "{}", "[]", "{\"Messages\":[],\"Extra\":true}", "{\"Messages\":[],\"Messages\":[]}" })
            Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.ParseMessageSchemas(Encoding.UTF8.GetBytes(invalid)));
    }

    [Fact]
    public void ScriptParserAcceptsTheMaximumSupportedSchemaTree()
    {
        var index = Pack(Source("main.lua", ""));
        for (int schema = 0; schema < ScriptMessageLimits.MaxSchemasPerPack; schema++)
            index.Messages.Add(new ScriptMessageSchema { Name = "State" + schema, Direction = AnomalyMessageDirection.ServerToClient,
                Fields = Enumerable.Range(0, ScriptMessageLimits.MaxFields).Select(field =>
                    new ScriptMessageField { Name = "Field" + field, Type = "Boolean" }).ToList() });
        byte[] bytes = ScriptPackIndexCodec.Encode(index);
        var parsed = ScriptPackIndexCodec.Parse(bytes);
        Assert.Equal(ScriptMessageLimits.MaxSchemasPerPack, parsed.Messages.Count);
        Assert.All(parsed.Messages, schema => Assert.Equal(ScriptMessageLimits.MaxFields, schema.Fields.Count));
    }

    [Theory]
    [InlineData("array")]
    [InlineData("object")]
    [InlineData("depth")]
    [InlineData("string")]
    public void ScriptParserRejectsOversizedShapesBeforeConstructingAnAssetTree(string shape)
    {
        string json = shape switch
        {
            "array" => "{\"Messages\":[" + string.Join(",", Enumerable.Repeat("{}", 33)) + "]}",
            "object" => "{\"A\":0,\"B\":0,\"C\":0,\"D\":0,\"E\":0,\"F\":[{}]}",
            "depth" => "{\"Messages\":[[[[[[]]]]]]}",
            _ => "{\"Messages\":[],\"Unexpected\":\"" + new string('x', AssetAddress.MaxLength + 1) + "\"}",
        };
        string reason = shape == "object" ? "object property count" : shape == "depth" ? "nesting or item count" : shape;
        var exception = Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.ParseMessageSchemas(Encoding.UTF8.GetBytes(json)));
        Assert.Contains("JSON " + reason, exception.Message);
    }

    [Theory]
    [InlineData("nul")]
    [InlineData("escape")]
    [InlineData("del")]
    [InlineData("control")]
    [InlineData("bom")]
    [InlineData("invalid-utf8")]
    [InlineData("dump")]
    [InlineData("oversize")]
    public void SourcePolicyRejectsBinaryAndOversizeInputsBeforeExecution(string invalid)
    {
        byte[] bytes = invalid == "invalid-utf8" ? new byte[] { 0xc0, 0xaf } : Encoding.UTF8.GetBytes(invalid switch
        {
            "nul" => "--\0", "escape" => "\u001bLua", "del" => "--\u007f", "control" => "\u0001", "bom" => "\ufeffreturn 1",
            "dump" => "--MoonSharp_dump_b64::", "oversize" => new string('x', 65537), _ => "",
        });
        Assert.Throws<InvalidDataException>(() => ScriptSourceValidator.Validate(bytes));
    }

    [Fact]
    public void SourcePolicyPreservesExactUtf8BytesAndAllowsEmptySources()
    {
        const string source = "-- é😀\r\nlocal Value = 1\t\n";
        Assert.Equal(source, ScriptSourceValidator.Validate(Encoding.UTF8.GetBytes(source)));
        Assert.Equal("", ScriptSourceValidator.Validate(Array.Empty<byte>()));
        Assert.Equal(ScriptSourceValidator.EmptySha256Hex, ScriptSourceValidator.HashHex(Array.Empty<byte>()));
    }

    [Fact]
    public void CatalogRoundTripBindsLogicalSourcesAndDeduplicatesPhysicalBytes()
    {
        var first = CatalogSource("a.lua", "return 1");
        var second = CatalogSource("b.lua", "return 1");
        var empty = CatalogSource("empty.lua", "");
        var catalog = Catalog(first, second, empty);
        var message = new ContentCatalogMessage { Catalog = catalog };
        using var stream = new MemoryStream();
        message.Serialize(new BinaryWriter(stream));
        stream.Position = 0;
        var copy = new ContentCatalogMessage(); copy.Deserialize(new BinaryReader(stream));
        Assert.Equal(3, Assert.Single(copy.Catalog.ScriptPacks).Sources.Count);
        Assert.Equal(catalog.CatalogSha256, copy.Catalog.CatalogSha256);
        copy.Catalog.ScriptPacks[0].Sources[0].Size++;
        Assert.Throws<InvalidDataException>(() => ContentCatalogMessage.ValidateCatalog(copy.Catalog, true));
    }

    [Theory]
    [InlineData("duplicate-address")]
    [InlineData("conflicting-size")]
    [InlineData("cross-kind")]
    [InlineData("empty-hash")]
    [InlineData("shared-id")]
    [InlineData("order")]
    [InlineData("ninth-source")]
    public void CatalogRejectsConflictingLogicalAndPhysicalIdentities(string invalid)
    {
        var catalog = Catalog(CatalogSource("a.lua", "return 1"), CatalogSource("b.lua", "return 1"));
        CatalogScriptPack pack = catalog.ScriptPacks[0];
        switch (invalid)
        {
            case "duplicate-address": pack.Sources[1].Address = pack.Sources[0].Address; break;
            case "conflicting-size": pack.Sources[1].Size++; break;
            case "cross-kind": pack.IndexHash = pack.Sources[0].Hash; break;
            case "empty-hash": pack.Sources[0].Size = 0; break;
            case "shared-id": catalog.Packs.Add(new CatalogPack { PackId = pack.PackId, IndexHash = Enumerable.Repeat((byte)0x21, 32).ToArray(), IndexSize = 10,
                BundleHash = Enumerable.Repeat((byte)0x22, 32).ToArray(), BundleSize = 10 }); break;
            case "order": pack.Sources.Reverse(); break;
            case "ninth-source": pack.Sources = Enumerable.Range(0, 9).Select(i => CatalogSource(i + ".lua", "")).ToList(); break;
        }
        Assert.Throws<InvalidDataException>(() => ContentCatalogMessage.ValidateCatalog(catalog, false));
    }

    [Theory]
    [InlineData(FileContentKind.ScriptIndex)]
    [InlineData(FileContentKind.LuaSource)]
    public void ManifestHasDistinctContentHashedScriptKinds(FileContentKind kind)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("return 1");
        byte[] hash = ScriptSourceValidator.Sha256(bytes);
        string name = "scripts/" + ScriptSourceValidator.HashHex(bytes) + (kind == FileContentKind.ScriptIndex ? ".scripts.json" : ".lua");
        var manifest = new FileManifestMessage { SessionId = 1, Files = new List<FileManifestMessage.FileEntry> { new FileManifestMessage.FileEntry(name, hash, (uint)bytes.Length, kind) } };
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
        using var stream = new MemoryStream(); manifest.Serialize(new BinaryWriter(stream)); stream.Position = 0;
        var copy = new FileManifestMessage(); copy.Deserialize(new BinaryReader(stream));
        Assert.Equal(kind, Assert.Single(copy.Files).Kind);
        var snapshot = new AssetManifestSnapshot(copy.Files);
        Assert.True(snapshot.Contains(hash, kind));
        manifest.Files[0] = new FileManifestMessage.FileEntry("scripts/wrong.lua", hash, (uint)bytes.Length, kind);
        Assert.Throws<InvalidDataException>(() => FileManifestMessage.ComputeManifestSha256(manifest.Files));
        manifest.Files[0] = new FileManifestMessage.FileEntry(name, hash, 0, kind);
        Assert.Throws<InvalidDataException>(() => FileManifestMessage.ComputeManifestSha256(manifest.Files));
    }

    [Fact]
    public void LogicalDuplicateSourcesRequireOnePhysicalManifestIdentity()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("return 1");
        var file = new FileManifestMessage.FileEntry(ScriptSourceValidator.HashHex(bytes) + ".lua", ScriptSourceValidator.Sha256(bytes), (uint)bytes.Length, FileContentKind.LuaSource);
        var files = new List<FileManifestMessage.FileEntry> { file, file };
        Assert.Throws<InvalidDataException>(() => new AssetManifestSnapshot(files));
        var manifest = new FileManifestMessage { SessionId = 1, Files = files, ManifestSha256 = FileManifestMessage.ComputeManifestSha256(files) };
        Assert.Throws<InvalidDataException>(() => manifest.Serialize(new BinaryWriter(new MemoryStream())));
    }

    [Fact]
    public void SourcePacksRejectConflictingSizesForTheSamePhysicalHash()
    {
        var index = Pack(Source("a.lua", "return 1"), Source("b.lua", "return 1"));
        index.Sources[1].SourceBytes++;
        Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.Validate(index));
    }

    [Fact]
    public void CatalogHashBindsEverySourceDeclarationAndIndexIdentity()
    {
        var catalog = Catalog(CatalogSource("a.lua", "return 1"));
        byte[] expected = catalog.CatalogSha256;
        CatalogScriptPack pack = catalog.ScriptPacks[0];
        pack.IndexHash[0] ^= 1;
        Assert.NotEqual(expected, CatalogBuilder.ComputeCatalogHash(catalog));
        pack.IndexHash[0] ^= 1;
        pack.IndexSize++;
        Assert.NotEqual(expected, CatalogBuilder.ComputeCatalogHash(catalog));
        pack.IndexSize--;
        pack.Sources[0].Address = "mods/example/panel/renamed.lua";
        Assert.NotEqual(expected, CatalogBuilder.ComputeCatalogHash(catalog));
        pack.Sources[0].Address = "mods/example/panel/a.lua";
        pack.Sources[0].Hash[0] ^= 1;
        Assert.NotEqual(expected, CatalogBuilder.ComputeCatalogHash(catalog));
        pack.Sources[0].Hash[0] ^= 1;
        pack.Sources[0].Size++;
        Assert.NotEqual(expected, CatalogBuilder.ComputeCatalogHash(catalog));
    }

    private static ScriptPackIndex Pack(params ScriptPackSource[] sources) => new ScriptPackIndex { PackId = "example:panel", Sources = sources.ToList() };
    private static ScriptPackSource Source(string name, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return new ScriptPackSource { Address = "mods/example/panel/" + name, SourceBytes = bytes.Length, SourceSha256 = ScriptSourceValidator.HashHex(bytes) };
    }
    private static CatalogScriptSource CatalogSource(string name, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return new CatalogScriptSource { Address = "mods/example/panel/" + name, Size = (uint)bytes.Length, Hash = ScriptSourceValidator.Sha256(bytes) };
    }
    private static ContentCatalog Catalog(params CatalogScriptSource[] sources) => CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 1, 127, new byte[32], scriptPacks:
        new[] { new CatalogScriptPack { PackId = "example:panel", IndexHash = Enumerable.Repeat((byte)0x72, 32).ToArray(), IndexSize = 512, Sources = sources.ToList() } });
}
