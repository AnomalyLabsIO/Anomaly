using System.Linq;
using System.Text;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class AssetCapacityContractTests
{
    [Fact]
    public void Catalog_wire_and_canonical_digest_bind_the_declared_decoded_summary()
    {
        var catalog = CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 1, 0, new byte[32], new[] { Pack() });
        var message = new ContentCatalogMessage { Catalog = catalog };
        using var output = new MemoryStream();
        message.Serialize(new BinaryWriter(output));
        output.Position = 0;
        var decoded = new ContentCatalogMessage();
        decoded.Deserialize(new BinaryReader(output));
        Assert.Equal(5, decoded.Catalog.SchemaVersion);
        Assert.Equal(358376, Assert.Single(decoded.Catalog.Packs).DecodedBytes);
        catalog.Packs[0].DecodedBytes--;
        Assert.Throws<InvalidDataException>(() => ContentCatalogMessage.ValidateCatalog(catalog, true));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(268435457L)]
    public void Catalog_rejects_missing_or_out_of_profile_decoded_declarations(long bytes)
    {
        var pack = Pack(); pack.DecodedBytes = bytes;
        var catalog = CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 1, 0, new byte[32], new[] { pack });
        Assert.Throws<InvalidDataException>(() => ContentCatalogMessage.ValidateCatalog(catalog, false));
    }

    [Fact]
    public void Legacy_index_requires_an_explicit_authoring_rebuild()
    {
        var error = Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Parse(
            Encoding.UTF8.GetBytes("{\"SchemaVersion\":1}"), remote: false));
        Assert.Contains("re-export", error.Message);
    }

    private static CatalogPack Pack() => new()
    {
        PackId = "fixture:measured", IndexHash = Enumerable.Repeat((byte)1, 32).ToArray(), IndexSize = 512,
        BundleHash = Enumerable.Repeat((byte)2, 32).ToArray(), BundleSize = 1024, DecodedBytes = 358376,
    };
}
