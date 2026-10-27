using System.Linq;
using System.Text;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using InventorySystem;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class DescriptorItemLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "anomaly-descriptor-items-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _registrations = new();
    private string PackRoot => Path.Combine(_root, "example", "panel");

    [Fact]
    public void OneDescriptorComposesScriptsAssetsAndNativeItemsAtFirstOffer()
    {
        WritePack(combined: true);
        ContentRegistry.Add(Definition("example:programmatic"));
        using var snapshot = AssetPackSnapshot.Build(_root);
        Assert.False(ContentRegistry.Frozen);
        Assert.Empty(ServerContentRegistrar.RegisteredItems);
        Assert.Single(snapshot.CopyPacks());
        Assert.Single(snapshot.CopyScriptPacks());
        Assert.Single(snapshot.Items);
        var offer = Offer(snapshot);
        Assert.True(ContentRegistry.Materialized);
        Assert.Equal(new[] { "example:coin", "example:programmatic" }, offer.Entries.Select(entry => entry.Key));
        Assert.Equal(2, ServerContentRegistrar.RegisteredItems.Count);
        Assert.Equal("mods/example/icon", offer.Entries.Single(entry => entry.Key == "example:coin").IconAddress);
        Assert.Equal("example:assets", Assert.Single(offer.Packs).PackId);
        Assert.Equal("example:scripts", Assert.Single(offer.ScriptPacks).PackId);
        Assert.Throws<InvalidOperationException>(() => ContentRegistry.Add(Definition("example:late")));
    }

    [Fact]
    public void CollisionWithProgrammaticRegistrationFailsTheWholeNativeBatch()
    {
        WritePack(items: new JArray(Item("example:new"), Item("example:collision")));
        ContentRegistry.Add(Definition("example:collision"));
        using var snapshot = AssetPackSnapshot.Build(_root);
        Assert.Throws<InvalidOperationException>(() => Offer(snapshot));
        Assert.False(ContentRegistry.Materialized);
        Assert.Empty(ContentRegistry.ItemEntries);
        Assert.Empty(ServerContentRegistrar.RegisteredItems);
        Assert.False(ContentRegistry.TryGetId("example:new", out _));
        Assert.False(ContentRegistry.TryGetId("example:collision", out _));
        ContentRegistry.Reset();
        ContentRegistry.Add(Definition("example:only_programmatic"));
        using var empty = AssetPackSnapshot.Empty();
        Assert.Equal("example:only_programmatic", Assert.Single(Offer(empty).Entries).Key);
    }

    [Fact]
    public void NativeRegistrarFailureCannotPublishPartialIdsOrCatalog()
    {
        WritePack(items: new JArray(Item("example:first"), Item("example:second")));
        ServerContentRegistrar.AcceptItems = false;
        using var snapshot = AssetPackSnapshot.Build(_root);
        Assert.Throws<InvalidOperationException>(() => Offer(snapshot));
        Assert.False(ContentRegistry.Materialized);
        Assert.Empty(ContentRegistry.Catalog.Entries);
        Assert.Empty(ContentRegistry.ItemEntries);
        Assert.False(ContentRegistry.TryGetId("example:first", out _));
        Assert.Empty(ServerContentRegistrar.RegisteredItems);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("key")]
    [InlineData("icon")]
    [InlineData("add")]
    [InlineData("remove")]
    public void FrozenDefinitionDriftRejectsFutureOffersWithoutRevokingPriorNativeState(string change)
    {
        WritePack(combined: true);
        using var approved = AssetPackSnapshot.Build(_root);
        var original = Offer(approved);
        int assigned = Assert.Single(original.Entries).AssignedId;
        var items = new JArray(Item());
        items[0]["IconAddress"] = "mods/example/icon";
        switch (change)
        {
            case "name": items[0]["DisplayName"] = "Changed"; break;
            case "description": items[0]["Description"] = "Changed"; break;
            case "key": items[0]["Key"] = "example:changed"; break;
            case "icon": ((JObject)items[0]).Remove("IconAddress"); break;
            case "add": items.Add(Item("example:another")); break;
            case "remove": items.Clear(); break;
        }
        WriteItems(items);
        using var changed = AssetPackSnapshot.Build(_root);
        Assert.Throws<InvalidOperationException>(() => ContentRegistry.ValidateSnapshotDefinitions(changed));
        Assert.Throws<InvalidOperationException>(() => Offer(changed));
        Assert.True(ContentRegistry.Materialized);
        Assert.True(ContentRegistry.TryGetId("example:coin", out int retained));
        Assert.Equal(assigned, retained);
        Assert.Single(ServerContentRegistrar.RegisteredItems);
        Assert.Equal(original.CatalogSha256, Offer(approved).CatalogSha256);
    }

    [Fact]
    public void ReorderedDefinitionsAndSourceOnlyRefreshKeepFrozenIds()
    {
        WritePack(combined: true, items: new JArray(Item("example:z"), Item("example:a")));
        using var approved = AssetPackSnapshot.Build(_root);
        var original = Offer(approved);
        WriteItems(new JArray(Item("example:a"), Item("example:z")));
        File.WriteAllText(Path.Combine(PackRoot, "main.lua"), "return 2", new UTF8Encoding(false));
        using var refreshed = AssetPackSnapshot.Build(_root);
        ContentRegistry.ValidateSnapshotDefinitions(refreshed);
        var next = Offer(refreshed);
        Assert.Equal(original.Entries.Select(entry => entry.AssignedId), next.Entries.Select(entry => entry.AssignedId));
        Assert.NotEqual(original.ManifestSha256, next.ManifestSha256);
        Assert.Equal(2, ServerContentRegistrar.RegisteredItems.Count);
    }

    [Theory]
    [InlineData("NotAVanillaItem")]
    [InlineData("coin")]
    [InlineData("0")]
    public void UnsupportedVanillaTemplatesFailBeforeFreeze(string template)
    {
        var item = Item();
        item["BaseTemplate"] = template;
        WritePack(items: new JArray(item));
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
        Assert.False(ContentRegistry.Frozen);
        Assert.Empty(ServerContentRegistrar.RegisteredItems);
    }

    [Fact]
    public void FrozenEmbeddedDisposalIsIdempotentAndBlocksOffersWithoutUnregisteringNativeItems()
    {
        var registration = ContentPacks.RegisterEmbedded(typeof(DescriptorItemLifecycleTests).Assembly, "Test.Items");
        _registrations.Add(registration);
        using var approved = AssetPackSnapshot.Build(null);
        Offer(approved);
        long revision = ContentPacks.Revision;
        registration.Dispose();
        Assert.Equal(revision + 1, ContentPacks.Revision);
        registration.Dispose();
        Assert.Equal(revision + 1, ContentPacks.Revision);
        Assert.Throws<InvalidOperationException>(() => ContentPacks.RegisterEmbedded(typeof(DescriptorItemLifecycleTests).Assembly, "Test.LateItems"));
        Assert.Equal(revision + 1, ContentPacks.Revision);
        using var future = AssetPackSnapshot.Build(null);
        Assert.Empty(future.Items);
        Assert.Throws<InvalidOperationException>(() => Offer(future));
        Assert.True(ContentRegistry.Materialized);
        Assert.True(ContentRegistry.TryGetId("embedded:coin", out _));
        Assert.Single(ServerContentRegistrar.RegisteredItems);
    }

    private static ContentCatalog Offer(AssetPackSnapshot snapshot) => ContentRegistry.CreateOfferCatalog(snapshot.BuildManifest(1).ManifestSha256, snapshot);
    private static CustomItemDefinition Definition(string key) => new() { Key = key, BaseTemplate = ItemType.Coin, DisplayName = key };
    private static JObject Item(string key = "example:coin") => new()
    {
        ["Key"] = key, ["BaseTemplate"] = "Coin", ["DisplayName"] = "Coin", ["Description"] = "Original",
    };
    private void WritePack(bool combined = false, JArray items = null)
    {
        Directory.CreateDirectory(PackRoot);
        var descriptor = new JObject
        {
            ["SchemaVersion"] = 1, ["PackId"] = "example:panel", ["Version"] = "1.0.0",
            ["Data"] = new JObject { ["Items"] = "items.json" },
        };
        items ??= new JArray(Item());
        if (combined)
        {
            descriptor["Scripts"] = new JObject { ["PackId"] = "example:scripts", ["EntryPoint"] = "main.lua" };
            descriptor["Assets"] = new JObject { ["PackId"] = "example:assets", ["Index"] = "content.index.json" };
            File.WriteAllText(Path.Combine(PackRoot, "main.lua"), "return 1", new UTF8Encoding(false));
            byte[] bundle = { 1, 2, 3 };
            File.WriteAllBytes(Path.Combine(PackRoot, "content.bundle"), bundle);
            string index = "{\"SchemaVersion\":1,\"PackId\":\"example:assets\",\"Namespace\":\"example\",\"UnityVersion\":\"6000.0.43f1\",\"Platform\":\"windows-x64\",\"GameProfile\":\"" + AssetPackIndex.SupportedGameProfile +
                "\",\"BundleFile\":\"content.bundle\",\"BundleSha256\":\"" + ScriptSourceValidator.HashHex(bundle) + "\",\"BundleBytes\":3,\"Assets\":[{\"Address\":\"mods/example/icon\",\"EntryName\":\"mods/example/icon\",\"Kind\":\"Texture2D\",\"Width\":2,\"Height\":2}],\"Visuals\":[]}";
            File.WriteAllText(Path.Combine(PackRoot, "content.index.json"), index, new UTF8Encoding(false));
            if (items.Count == 1 && (string)items[0]["Key"] == "example:coin") items[0]["IconAddress"] = "mods/example/icon";
        }
        File.WriteAllText(Path.Combine(PackRoot, "pack.json"), descriptor.ToString(), new UTF8Encoding(false));
        WriteItems(items);
    }
    private void WriteItems(JArray items) => File.WriteAllText(Path.Combine(PackRoot, "items.json"),
        new JObject { ["SchemaVersion"] = 1, ["Items"] = items }.ToString(), new UTF8Encoding(false));
    public void Dispose()
    {
        ContentRegistry.Reset();
        ServerContentRegistrar.AcceptItems = true;
        foreach (var registration in _registrations) registration.Dispose();
        string spool = Path.Combine(_root, ".anomaly-spool");
        Assert.True(System.Threading.SpinWait.SpinUntil(() => !Directory.Exists(spool) || !Directory.EnumerateDirectories(spool).Any(), 5000));
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
