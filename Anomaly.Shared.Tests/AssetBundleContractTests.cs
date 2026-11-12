using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class AssetBundleContractTests
{
    [Theory]
    [InlineData("game/skin/logo")]
    [InlineData("game/skin/background")]
    [InlineData("game/cassie/.g7")]
    [InlineData("mods/example/images/logo.png")]
    [InlineData("mods/example/sound.with.dots")]
    public void AddressesPreserveExactLogicalIdentity(string value)
    {
        Assert.Equal(value, AssetAddress.Parse(value).Value);
        Assert.Equal(AssetAddress.Parse(value), AssetAddress.Parse(value));
    }

    [Theory]
    [InlineData("game/cassie/.")]
    [InlineData("game/cassie/not-a-stock-word")]
    [InlineData("game/skin/unknown")]
    [InlineData("Game/skin/logo")]
    [InlineData("mods/example/../logo")]
    [InlineData("mods/example//logo")]
    [InlineData("mods/example/./logo")]
    [InlineData("mods/example\\logo")]
    [InlineData("mods/example/%2e%2e/logo")]
    [InlineData(" mods/example/logo")]
    [InlineData("mods/example/logo/")]
    public void InvalidAddressesAreNotNormalizedIntoValidOnes(string value) => Assert.False(AssetAddress.TryParse(value, out _));

    [Fact]
    public void CassieGlitchAddressKeepsNativeClipTimingSeparateFromScheduling()
    {
        Assert.True(StockAssetProfile.TryGetCassie(AssetAddress.Parse("game/cassie/.g7"), out var timing));
        Assert.Equal(".G7", timing.ApiName);
        Assert.Equal(1.5f, timing.SchedulingDurationSeconds);
        Assert.Equal(72000, timing.SampleFrames);
        Assert.Equal(44100, timing.SampleRate);
        Assert.True(timing.MatchesClip(72000, 44100));
        Assert.False(timing.MatchesClip(66150, 44100));
        var pack = Pack(Audio("game/cassie/.g7", 72000, 44100));
        AssetPackIndexCodec.Validate(pack, true);
        pack.Assets[0].SampleFrames = 66150;
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, true));
    }

    [Fact]
    public void SkinTargetsAreLocalOnlyAndRequireTexture()
    {
        var pack = Pack(Texture("game/skin/logo"));
        AssetPackIndexCodec.Validate(pack, false);
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, true));
        pack.Assets[0] = Audio("game/skin/logo", 12000, 48000);
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, false));
    }

    [Fact]
    public void DuplicateAddressesWrongNamespacesAndPhysicalAliasesAreRejected()
    {
        var pack = Pack(Texture("mods/example/logo"));
        pack.Assets.Add(Texture("mods/example/logo"));
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, true));
        pack.Assets.RemoveAt(1);
        pack.Assets[0].EntryName = "assets/source/logo.png";
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, true));
        pack.Assets[0] = Texture("mods/someoneelse/logo");
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, true));
    }

    [Fact]
    public void IndexIsStrictBoundedDataAndRejectsScriptLikeExtensions()
    {
        byte[] valid = Json(Pack(Texture("mods/example/logo")));
        Assert.Single(AssetPackIndexCodec.Parse(valid, true).Assets);
        string text = Encoding.UTF8.GetString(valid);
        foreach (string malformed in new[] {
            text.Insert(1, "\"SchemaVersion\":1,"),
            text.Insert(1, "\"$type\":\"System.Diagnostics.Process\","),
            text + "{}", text.Replace("\"Width\":16", "\"Width\":1e999"),
            text.Replace("\"Width\":16", "\"Width\":16.5"),
            text.Replace("\"Width\":16", "\"Width\":016"),
            text.Replace("\"Width\":16", "\"Width\":NaN"),
            text.Replace("windows-x64", "linux-x64"),
            text.Replace("6000.0.43f1", "6000.0.44f1"),
            text.Replace("pack.bundle", "../pack.bundle"),
            text.Replace("mods/example/logo", "mods/example/\\ud800"),
            text.Insert(1, "\"Unknown\":" + new string('[', 18) + "0" + new string(']', 18) + ",") })
            Assert.ThrowsAny<Exception>(() => AssetPackIndexCodec.Parse(Encoding.UTF8.GetBytes(malformed), true));
        Assert.ThrowsAny<Exception>(() => AssetPackIndexCodec.Parse(new byte[] { 0xff }, true));
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Parse(new byte[AssetPackIndexCodec.MaxIndexBytes + 1], true));
    }

    [Fact]
    public void VisualsRequireSamePackTypedReferencesAndBoundedAcyclicHierarchy()
    {
        var pack = Pack(Mesh());
        var visual = new DecorativeVisualDefinition { Address = "mods/example/visual" };
        visual.Nodes.Add(new DecorativeVisualNode { Name = "root", MeshAddress = "mods/example/mesh", MaterialPreset = "opaque-unlit" });
        pack.Visuals.Add(visual);
        AssetPackIndexCodec.Validate(pack, true);
        Assert.Equal(2, AssetPackIndexCodec.GetTriangleCount(pack, visual));
        visual.Nodes[0].Parent = 0;
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, true));
        visual.Nodes[0].Parent = -1;
        visual.Nodes[0].MaterialPreset = "custom-shader";
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, true));
        visual.Nodes[0].MaterialPreset = "opaque-lit";
        visual.Nodes[0].MeshAddress = "mods/other/mesh";
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, true));
        visual.Nodes[0].MeshAddress = "mods/example/mesh";
        for (int i = 1; i <= 16; i++) visual.Nodes.Add(new DecorativeVisualNode { Name = "node", Parent = i - 1 });
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, true));
        visual.Nodes.RemoveAt(16);
        AssetPackIndexCodec.Validate(pack, true);
        visual.Nodes[0].Scale = new[] { 100f, 100f, 100f };
        visual.Nodes[1].Scale = new[] { 100f, 100f, 100f };
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, true));
    }

    [Theory]
    [InlineData("MonoBehaviour")]
    [InlineData("GameObject")]
    [InlineData("Material")]
    [InlineData("Shader")]
    [InlineData("AnimationClip")]
    public void UnsupportedRootTypesCannotBeDeclared(string kind)
    {
        var pack = Pack(Texture("mods/example/logo"));
        pack.Assets[0].Kind = kind;
        Assert.Throws<InvalidDataException>(() => AssetPackIndexCodec.Validate(pack, true));
    }

    [Fact]
    public void CatalogDigestBindsIndexBundleAndIconAddress()
    {
        var catalog = CatalogBuilder.Build(new[] { new CatalogEntry { Key = "example:item", DisplayName = "Test", BaseTemplateId = 0,
            IconAddress = "mods/example/icon" } }, 1, 127, new byte[32], new[] { CatalogPack() });
        ContentCatalogMessage.ValidateCatalog(catalog, true);
        byte[] original = (byte[])catalog.CatalogSha256.Clone();
        catalog.Packs[0].IndexHash[0] ^= 1;
        Assert.False(CatalogBuilder.HashesEqual(original, CatalogBuilder.ComputeCatalogHash(catalog)));
        Assert.Throws<InvalidDataException>(() => ContentCatalogMessage.ValidateCatalog(catalog, true));
        catalog.Packs[0].IndexHash[0] ^= 1;
        catalog.Packs[0].BundleHash[0] ^= 1;
        Assert.False(CatalogBuilder.HashesEqual(original, CatalogBuilder.ComputeCatalogHash(catalog)));
        catalog.Packs[0].BundleHash[0] ^= 1;
        catalog.Entries[0].IconAddress = "mods/example/other";
        Assert.False(CatalogBuilder.HashesEqual(original, CatalogBuilder.ComputeCatalogHash(catalog)));
    }

    [Fact]
    public void DecorativeWirePreservesGenerationRevisionAndRejectsDuplicateIds()
    {
        var state = new DecorativeVisualInstance { Id = 7, Address = "mods/example/visual", Position = new[] { 1f, 2f, 3f } };
        var message = new DecorativeVisualSnapshotMessage { Generation = 3, Revision = 8, Instances = { state } };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        message.Serialize(writer);
        stream.Position = 0;
        var decoded = new DecorativeVisualSnapshotMessage();
        decoded.Deserialize(new BinaryReader(stream));
        Assert.Equal(3UL, decoded.Generation);
        Assert.Equal(8UL, decoded.Revision);
        Assert.Equal(state.Position, Assert.Single(decoded.Instances).Position);
        message.Instances.Add(state.Clone());
        Assert.Throws<InvalidDataException>(() => message.Serialize(writer));
        var update = new DecorativeVisualUpdateMessage { Generation = 3, Revision = 0, Operation = DecorativeVisualOperation.Remove, Instance = state };
        Assert.Throws<InvalidDataException>(() => update.Serialize(writer));
    }

    [Fact]
    public void ActivationFailureKeysIncludePackAndAddressIdentities()
    {
        var ack = new ContentCatalogAckMessage { CatalogSha256 = new byte[32], ManifestSha256 = new byte[32],
            Status = ContentCatalogActivationStatus.Failed, FailedKeys = { "example:pack", "mods/example/mesh", "game/cassie/.g7" } };
        using var stream = new MemoryStream();
        ack.Serialize(new BinaryWriter(stream));
        stream.Position = 0;
        var decoded = new ContentCatalogAckMessage();
        decoded.Deserialize(new BinaryReader(stream));
        Assert.Equal(ack.FailedKeys, decoded.FailedKeys);
    }

    [Theory]
    [InlineData(".G7")]
    [InlineData(".g7")]
    [InlineData(".")]
    [InlineData("custom word")]
    [InlineData("custom\0word")]
    public void NewVocabularyCannotShadowStockOrUseControlNames(string name)
    {
        Assert.False(AnnouncerVocabularyMessage.IsValidNewApiName(name));
        var message = new AnnouncerVocabularyMessage { Words = { new AnnouncerVocabularyMessage.Entry(name, "mods/example/tone", 0.25f, 0) } };
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => message.Serialize(new BinaryWriter(output)));
    }

    private static AssetPackIndex Pack(params PackAssetEntry[] assets) => new AssetPackIndex {
        PackId = "example:pack", Namespace = "example", BundleFile = "pack.bundle", BundleSha256 = new string('a', 64), BundleBytes = 100, DecodedBytes = 1024,
        Assets = assets.ToList() };
    private static PackAssetEntry Texture(string address) => new PackAssetEntry { Address = address, EntryName = address, Kind = "Texture2D", Width = 16, Height = 16 };
    private static PackAssetEntry Audio(string address, int frames, int rate) => new PackAssetEntry { Address = address, EntryName = address, Kind = "AudioClip", SampleFrames = frames, SampleRate = rate, Channels = 1 };
    private static PackAssetEntry Mesh() => new PackAssetEntry { Address = "mods/example/mesh", EntryName = "mods/example/mesh", Kind = "Mesh", VertexCount = 4, TriangleCount = 2 };
    private static CatalogPack CatalogPack() => new CatalogPack { PackId = "example:pack", IndexHash = Enumerable.Repeat((byte)1, 32).ToArray(), BundleHash = Enumerable.Repeat((byte)2, 32).ToArray(), IndexSize = 100, BundleSize = 100, DecodedBytes = 1024 };
    private static byte[] Json(AssetPackIndex index)
    {
        var json = JObject.FromObject(index);
        foreach (JObject entry in json["Assets"]) entry.Remove("AssetKind");
        return Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
    }
}
