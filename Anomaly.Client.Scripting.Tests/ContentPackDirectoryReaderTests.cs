using System.Text;
using Anomaly.Client.Content;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content.Packs;
using Anomaly.Shared.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ContentPackDirectoryReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Anomaly.ContentPackDirectoryTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DescriptorGeneratesCanonicalAddressHashAndMessageContracts()
    {
        ScriptPack("panel", "function Value() return 1 end", messages: true);
        Write("example/panel/scripts/messages.json", "{\"Messages\":[{\"Name\":\"State\",\"Direction\":\"ServerToClient\",\"Fields\":[]}]}");
        var source = Assert.Single(ContentPackDirectoryReader.Read(_root));
        Assert.Equal("example:panel_scripts", source.PackId);
        Assert.Equal("mods/example/panel/scripts/main.lua", source.Address);
        Assert.Equal(ScriptSourceValidator.HashHex(Encoding.UTF8.GetBytes(source.Source)), source.SourceSha256);
        Assert.Equal("State", Assert.Single(source.MessageSchemas!).Name);
        Write("example/panel/scripts/main.lua", "return 2");
        Assert.Equal("function Value() return 1 end", source.Source);
    }

    [Fact]
    public void MissingOrEmptyDirectoryIsAnEmptySourceSet()
    {
        Assert.Empty(ContentPackDirectoryReader.Read(_root));
        Directory.CreateDirectory(_root);
        Assert.Empty(ContentPackDirectoryReader.Read(_root));
    }

    [Fact]
    public void UnlistedHelpersIndexesAndAuthoringDirectoriesAreNotDiscovered()
    {
        ScriptPack("panel", "return 1");
        Write("example/panel/scripts/helper.lua", "\ufeffinvalid ignored helper");
        Write("example/panel/unused.index.json", "invalid ignored index");
        for (int number = 0; number < ContentPackCodec.MaxScannedEntries + 1; number++)
            Write("example/panel/authoring/" + number + ".lua", "");
        Write("root-file.lua", "ignored");
        Write("example/namespace-file.lua", "ignored");
        Assert.Single(ContentPackDirectoryReader.Read(_root));
    }

    [Theory]
    [InlineData("Example/panel")]
    [InlineData("example/other")]
    public void DirectoryIdentityMustExactlyMatchDescriptor(string directory)
    {
        Write(directory + "/pack.json", Descriptor("panel"));
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Fact]
    public void EveryPackDirectoryRequiresADescriptor()
    {
        Write("example/panel/main.lua", "return 1");
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Fact]
    public void RootSpoolIsNotAContentNamespace()
    {
        ScriptPack("panel", "return 1");
        Write(".anomaly-spool/not-a-pack/source.lua", "ignored");
        Assert.Single(ContentPackDirectoryReader.Read(_root));
    }

    [Theory]
    [InlineData("{\"SchemaVersion\":1}")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not json")]
    public void MalformedDescriptorFailsClosed(string text)
    {
        Write("example/panel/pack.json", text);
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Theory]
    [InlineData("../main.lua")]
    [InlineData("scripts/../main.lua")]
    [InlineData("scripts//main.lua")]
    [InlineData("scripts/Main.lua")]
    [InlineData("scripts\\main.lua")]
    [InlineData("/main.lua")]
    public void NoncanonicalEntrypointsFailClosed(string path)
    {
        Write("example/panel/pack.json", Descriptor("panel").Replace("scripts/main.lua", path.Replace("\\", "\\\\")));
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Fact]
    public void MissingDeclaredEntrypointFailsClosed()
    {
        Write("example/panel/pack.json", Descriptor("panel"));
        Assert.ThrowsAny<IOException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Theory]
    [InlineData("\ufeffreturn 1")]
    [InlineData("return '\0'")]
    public void InvalidTextRejectsTheWholeDirectory(string text)
    {
        ScriptPack("panel", text);
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourceCountAndIndividualSizeAreBounded(bool count)
    {
        if (count)
            for (int number = 0; number <= ScriptPackLimits.MaxScripts; number++) ScriptPack("pack" + number, "");
        else ScriptPack("panel", new string(' ', ScriptPackLimits.MaxScriptBytesPerFile + 1));
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Fact]
    public void DirectoryDiscoveryIsBounded()
    {
        for (int number = 0; number <= ContentPackCodec.MaxScannedEntries; number++) Write("file" + number, "");
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Fact]
    public void SourceReadRejectsExhaustedAggregateBudgetBeforeAllocating()
    {
        ScriptPack("panel", new string(' ', ScriptPackLimits.MaxScriptBytesPerFile));
        string path = Path.Combine(_root, "example/panel/scripts/main.lua");
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.ReadBounded(path, maximum: 0));
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.ReadBounded(path, maximum: 0));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 32 * 1024, "Rejected source read allocated " + allocated + " bytes.");
    }

    [Fact]
    public void AggregateSourceBytesAreBoundedAcrossPacks()
    {
        for (int pack = 0; pack < 5; pack++) ScriptPack("pack" + pack, new string(' ', ScriptPackLimits.MaxScriptBytesPerFile));
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Fact]
    public void MessageDescriptorCannotUseUnknownProperties()
    {
        ScriptPack("panel", "", messages: true);
        Write("example/panel/scripts/messages.json", "{\"Messages\":[],\"Other\":true}");
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Fact]
    public void AssetCaptureUsesOnlyDeclaredIndexAndDerivesIndexRelativeBundle()
    {
        AssetPack();
        Write("example/panel/assets/unlisted.index.json", "invalid ignored index");
        var pack = Assert.Single(ContentPackDirectoryReader.ReadPacks(_root));
        Assert.Empty(ContentPackDirectoryReader.Read(_root));
        Assert.Equal("example:panel_assets", pack.Contents.AssetIndex.PackId);
        Assert.Equal("assets/pack.bundle", pack.Contents.BundlePath);
        Write("example/panel/assets/pack.bundle", "bundle not read by capture");
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "example/panel/assets/pack.bundle")), pack.Resolve(pack.Contents.BundlePath));
        Assert.Throws<InvalidDataException>(() => pack.Resolve("../outside.bundle"));
    }

    [Fact]
    public void AssetIdentityMustMatchDeclaredComponent()
    {
        AssetPack();
        string path = Path.Combine(_root, "example/panel/assets/pack.index.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("example:panel_assets", "example:other"));
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.ReadPacks(_root));
    }

    [Fact]
    public void ScriptCountIsPreflightedBeforeAnyEntrypointRead()
    {
        for (int number = 0; number <= ScriptPackLimits.MaxScripts; number++)
            Write("example/pack" + number + "/pack.json", Descriptor("pack" + number));
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Fact]
    public void DuplicateComponentIdentitiesRejectTheCompleteSet()
    {
        ScriptPack("first", "return 1");
        ScriptPack("second", "return 2");
        Write("example/second/pack.json", Descriptor("second").Replace("second_scripts", "first_scripts"));
        Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(_root));
    }

    [Fact]
    public void LocalReservedSkinPackIdentityRemainsSupported()
    {
        AssetPack();
        string descriptor = File.ReadAllText(Path.Combine(_root, "example/panel/pack.json"))
            .Replace("example:panel", "anomaly:panel");
        string index = File.ReadAllText(Path.Combine(_root, "example/panel/assets/pack.index.json"))
            .Replace("example:panel_assets", "anomaly:panel_assets")
            .Replace("\"Namespace\":\"example\"", "\"Namespace\":\"anomaly\"")
            .Replace("mods/example/logo", "game/skin/logo");
        Directory.Delete(Path.Combine(_root, "example"), recursive: true);
        Write("anomaly/panel/pack.json", descriptor);
        Write("anomaly/panel/assets/pack.index.json", index);
        var pack = Assert.Single(ContentPackDirectoryReader.ReadPacks(_root));
        Assert.Equal("game/skin/logo", Assert.Single(pack.Contents.AssetIndex.Assets).Address);
    }

    [Fact]
    public void ReparseAncestorsAreRejectedForDiscoveryReadsAndBundles()
    {
        ScriptPack("panel", "return 1");
        string target = Path.Combine(_root, "example/panel/scripts");
        string link = Path.Combine(_root, "linked");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (UnauthorizedAccessException) { return; } // Windows requires developer mode or symlink privilege.
        try
        {
            Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.ReadBounded(Path.Combine(link, "main.lua"), 1024));
            Assert.Throws<InvalidDataException>(() => ContentPackDirectoryReader.Read(link));
            var pack = new LocalContentPack(_root, null!);
            Assert.Throws<InvalidDataException>(() => pack.Resolve("linked/main.lua"));
        }
        finally { Directory.Delete(link); }
    }

    private void ScriptPack(string name, string text, bool messages = false)
    {
        Write("example/" + name + "/pack.json", Descriptor(name, messages));
        Write("example/" + name + "/scripts/main.lua", text);
    }

    private static string Descriptor(string name, bool messages = false) =>
        "{\"SchemaVersion\":1,\"PackId\":\"example:" + name + "\",\"Version\":\"1.0.0\",\"Scripts\":{\"PackId\":\"example:" + name + "_scripts\",\"EntryPoint\":\"scripts/main.lua\"" +
        (messages ? ",\"Messages\":\"scripts/messages.json\"" : "") + "}}";

    private void AssetPack()
    {
        Write("example/panel/pack.json", "{\"SchemaVersion\":1,\"PackId\":\"example:panel\",\"Version\":\"1.0.0\",\"Assets\":{\"PackId\":\"example:panel_assets\",\"Index\":\"assets/pack.index.json\"}}");
        var index = new AssetPackIndex
        {
            PackId = "example:panel_assets", Namespace = "example", BundleFile = "pack.bundle", BundleSha256 = new string('a', 64), BundleBytes = 100,
            Assets = { new PackAssetEntry { Address = "mods/example/logo", EntryName = "mods/example/logo", Kind = "Texture2D", Width = 16, Height = 16 } }
        };
        var json = JObject.FromObject(index);
        foreach (JObject entry in json["Assets"]!) entry.Remove("AssetKind");
        Write("example/panel/assets/pack.index.json", json.ToString(Formatting.None));
    }

    private void Write(string path, string text)
    {
        string target = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, text, new UTF8Encoding(false, true));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
