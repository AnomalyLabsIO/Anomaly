using System.Text;
using Anomaly.Client.Content;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Scripting;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ScriptPackReaderTests
{
    [Fact]
    public void ScriptOnlyOfferCapturesSourcesAndBoundMessageSchemas()
    {
        var fixture = new PackFixture("return 'héllo'");
        fixture.Index.Messages.Add(new ScriptMessageSchema { Name = "State", Direction = AnomalyMessageDirection.ServerToClient,
            Fields = new() { new ScriptMessageField { Name = "Value", Type = "Boolean" } } });
        fixture.Publish();
        var source = Assert.Single(ScriptPackReader.Read(fixture.Catalog, fixture.Read));
        Assert.Equal("return 'héllo'", source.Source);
        Assert.Equal("mods/example/panel/0.lua", source.Address);
        Assert.Equal(ScriptSourceValidator.HashHex(fixture.IndexBytes), source.IndexSha256);
        Assert.Equal("State", Assert.Single(source.MessageSchemas!).Name);
        fixture.Index.Messages[0].Name = "Changed";
        Assert.Equal("State", source.MessageSchemas![0].Name);
    }

    [Fact]
    public void LogicalEmptySourceNeedsOnlyItsPhysicalIndex()
    {
        var fixture = new PackFixture("");
        var source = Assert.Single(ScriptPackReader.Read(fixture.Catalog, fixture.Read));
        Assert.Equal("", source.Source);
        Assert.Equal(ScriptSourceValidator.EmptySha256Hex, source.SourceSha256);
        Assert.Equal(new[] { FileContentKind.ScriptIndex }, fixture.KindsRead);
    }

    [Fact]
    public void IdenticalBytesCanBackDistinctLogicalSources()
    {
        var fixture = new PackFixture("return 1", "return 1");
        var sources = ScriptPackReader.Read(fixture.Catalog, fixture.Read);
        Assert.Equal(2, sources.Count);
        Assert.NotEqual(sources[0].Address, sources[1].Address);
        Assert.Equal(sources[0].SourceSha256, sources[1].SourceSha256);
        Assert.Same(sources[0].Source, sources[1].Source);
        Assert.Equal(1, fixture.KindsRead.Count(kind => kind == FileContentKind.LuaSource));
        Assert.Equal(2, fixture.Files.Count); // One index and one source blob.
    }

    [Fact]
    public void EntirePackMetadataIsCheckedBeforeAllocatingSourceSnapshots()
    {
        var fixture = new PackFixture(new string(' ', ScriptPackLimits.MaxScriptBytesPerFile), "return 2");
        fixture.Catalog.ScriptPacks[0].Sources[1].Size++;
        fixture.Catalog.CatalogSha256 = CatalogBuilder.ComputeCatalogHash(fixture.Catalog);
        Assert.Throws<InvalidDataException>(() => ScriptPackReader.Read(fixture.Catalog, fixture.Read));
        Assert.Equal(new[] { FileContentKind.ScriptIndex }, fixture.KindsRead);
    }

    [Fact]
    public void MalformedDescriptorCannotAllocateAnAssetSizedObjectTree()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"One\":null,\"Two\":null,\"Three\":null,\"Four\":null,\"Five\":null,\"Six\":[" +
            string.Join(",", Enumerable.Repeat("{}", 4096)) + "]}");
        // Warm reflection/exception paths before measuring this parser call.
        Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.ParseMessageSchemas(bytes));
        long before = GC.GetAllocatedBytesForCurrentThread();
        var exception = Assert.Throws<InvalidDataException>(() => ScriptPackIndexCodec.ParseMessageSchemas(bytes));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Contains("JSON object property count", exception.Message);
        Assert.True(allocated < 64 * 1024, "Malformed descriptor allocated " + allocated + " bytes.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangedCachedBytesCannotReachTheInterpreter(bool index)
    {
        var fixture = new PackFixture("return 1");
        byte[] hash = index ? fixture.Catalog.ScriptPacks[0].IndexHash : fixture.Catalog.ScriptPacks[0].Sources[0].Hash;
        fixture.Files[Hex(hash)][0] ^= 1;
        Assert.Throws<InvalidDataException>(() => ScriptPackReader.Read(fixture.Catalog, fixture.Read));
    }

    [Theory]
    [InlineData("size")]
    [InlineData("hash")]
    [InlineData("address")]
    public void AuthenticatedCatalogAndIndexMustAgreeOnEverySource(string field)
    {
        var fixture = new PackFixture("return 1");
        var source = fixture.Catalog.ScriptPacks[0].Sources[0];
        if (field == "size") source.Size++;
        if (field == "hash") source.Hash[0] ^= 1;
        if (field == "address") source.Address = "mods/example/panel/other.lua";
        fixture.Catalog.CatalogSha256 = CatalogBuilder.ComputeCatalogHash(fixture.Catalog);
        Assert.Throws<InvalidDataException>(() => ScriptPackReader.Read(fixture.Catalog, fixture.Read));
    }

    [Fact]
    public void IndexPackIdentityMustMatchItsAuthenticatedOffer()
    {
        var fixture = new PackFixture("return 1");
        fixture.Index.PackId = "example:other";
        fixture.Publish();
        fixture.Catalog.ScriptPacks[0].PackId = "example:panel";
        fixture.Catalog.CatalogSha256 = CatalogBuilder.ComputeCatalogHash(fixture.Catalog);
        Assert.Throws<InvalidDataException>(() => ScriptPackReader.Read(fixture.Catalog, fixture.Read));
    }

    [Theory]
    [InlineData("\ufeffreturn 1")]
    [InlineData("return '\0'")]
    [InlineData("-- MoonSharp_dump_b64::")]
    public void CorrectHashDoesNotAuthorizeBinaryOrCompiledSource(string text)
    {
        var fixture = new PackFixture(text);
        Assert.Throws<InvalidDataException>(() => ScriptPackReader.Read(fixture.Catalog, fixture.Read));
    }

    [Fact]
    public void NoScriptOfferPerformsNoFileReads()
    {
        var catalog = CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 1, 0, new byte[32]);
        Assert.Empty(ScriptPackReader.Read(catalog, (_, _) => throw new InvalidOperationException()));
    }

    private sealed class PackFixture
    {
        internal ScriptPackIndex Index = new() { PackId = "example:panel" };
        internal ContentCatalog Catalog = null!;
        internal byte[] IndexBytes = null!;
        internal Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
        internal List<FileContentKind> KindsRead = new();
        internal PackFixture(params string[] scripts)
        {
            for (int number = 0; number < scripts.Length; number++)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(scripts[number]);
                string hash = ScriptSourceValidator.HashHex(bytes);
                Files[hash] = bytes;
                Index.Sources.Add(new ScriptPackSource { Address = "mods/example/panel/" + number + ".lua", SourceSha256 = hash, SourceBytes = bytes.Length });
            }
            Publish();
        }
        internal void Publish()
        {
            IndexBytes = ScriptPackIndexCodec.Encode(Index);
            byte[] indexHash = ScriptSourceValidator.Sha256(IndexBytes);
            Files[Hex(indexHash)] = IndexBytes;
            Catalog = CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 1, 0, new byte[32], scriptPacks: new[]
            {
                new CatalogScriptPack { PackId = Index.PackId, IndexHash = indexHash, IndexSize = (uint)IndexBytes.Length,
                    Sources = Index.Sources.Select(source => new CatalogScriptSource { Address = source.Address,
                        Hash = Convert.FromHexString(source.SourceSha256), Size = (uint)source.SourceBytes }).ToList() },
            });
        }
        internal byte[] Read(byte[] hash, FileContentKind kind) { KindsRead.Add(kind); return Files[Hex(hash)]; }
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
