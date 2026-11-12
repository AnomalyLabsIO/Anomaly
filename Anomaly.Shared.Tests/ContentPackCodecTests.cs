using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content.Packs;
using Anomaly.Shared.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ContentPackCodecTests
{
    [Fact]
    public void CompositePackBindsOneEntrypointContractsAssetIdentityAndTypedItems()
    {
        var files = Files();
        var capture = Capture(Descriptor(), files);
        var index = ScriptPackIndexCodec.Parse(capture.ScriptIndexBytes);
        var source = Assert.Single(index.Sources);
        Assert.Equal("example:panel_scripts", index.PackId);
        Assert.Equal("mods/example/panel/scripts/main.lua", source.Address);
        Assert.Equal(ScriptSourceValidator.HashHex(files["scripts/main.lua"]), source.SourceSha256);
        Assert.Equal("State", Assert.Single(index.Messages).Name);
        Assert.Equal("assets/panel.bundle", capture.BundlePath);
        Assert.Equal("example:panel_assets", capture.AssetIndex.PackId);
        var item = Assert.Single(capture.Items);
        Assert.Equal("example:coin", item.Key);
        Assert.Equal("Coin", item.BaseTemplate);
        Assert.Equal("mods/example/icon", item.IconAddress);
    }

    [Fact]
    public void MessageContractChangesBindTheGeneratedScriptIndex()
    {
        var files = Files();
        string first = ScriptSourceValidator.HashHex(Capture(Descriptor(), files).ScriptIndexBytes);
        files["scripts/messages.json"] = Bytes("{\"Messages\":[{\"Name\":\"Changed\",\"Direction\":\"ServerToClient\",\"Fields\":[]}]}");
        string second = ScriptSourceValidator.HashHex(Capture(Descriptor(), files).ScriptIndexBytes);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("unknown-script")]
    [InlineData("missing-version")]
    [InlineData("schema")]
    [InlineData("empty")]
    [InlineData("null-section")]
    [InlineData("foreign-component")]
    [InlineData("duplicate-component")]
    [InlineData("source-path")]
    [InlineData("same-metadata-path")]
    [InlineData("manifest-as-items")]
    [InlineData("version-leading-zero")]
    [InlineData("version-prerelease")]
    [InlineData("version-missing-part")]
    public void AmbiguousOrUnsupportedDescriptorsAreRejectedBeforeCapture(string invalid)
    {
        JObject json = Descriptor();
        switch (invalid)
        {
            case "unknown": json["Dependencies"] = new JArray(); break;
            case "unknown-script": json["Scripts"]["Modules"] = new JArray(); break;
            case "missing-version": json.Remove("Version"); break;
            case "schema": json["SchemaVersion"] = 2; break;
            case "empty": json.Remove("Scripts"); json.Remove("Assets"); json.Remove("Data"); break;
            case "null-section": json["Scripts"] = JValue.CreateNull(); break;
            case "foreign-component": json["Scripts"]["PackId"] = "other:panel"; break;
            case "duplicate-component": json["Assets"]["PackId"] = "example:panel_scripts"; break;
            case "source-path": json["Scripts"]["EntryPoint"] = "../main.lua"; break;
            case "same-metadata-path": json["Data"]["Items"] = "scripts/messages.json"; break;
            case "manifest-as-items": json["Data"]["Items"] = "pack.json"; break;
            case "version-leading-zero": json["Version"] = "01.0.0"; break;
            case "version-prerelease": json["Version"] = "1.0.0-beta"; break;
            case "version-missing-part": json["Version"] = "1.0"; break;
        }
        Assert.Throws<InvalidDataException>(() => ContentPackCodec.Parse(JsonBytes(json)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/main.lua")]
    [InlineData("scripts\\main.lua")]
    [InlineData("scripts//main.lua")]
    [InlineData("scripts/../main.lua")]
    [InlineData("scripts/./main.lua")]
    [InlineData("scripts/Main.lua")]
    [InlineData("c:/main.lua")]
    [InlineData("https://host/main.lua")]
    [InlineData("scripts/main.lua/")]
    [InlineData("scripts/caf\u00e9.lua")]
    [InlineData("a/b/c/d/e/f/g/h/i.lua")]
    public void PathsAreCanonicalAndOperatingSystemIndependent(string path)
        => Assert.False(ContentPackCodec.IsRelativePath(path));

    [Fact]
    public void MetadataLimitsRejectInvalidUtf8DuplicatePropertiesAndOversizedDocuments()
    {
        Assert.Throws<InvalidDataException>(() => ContentPackCodec.Parse(new byte[] { 0xff }));
        Assert.Throws<InvalidDataException>(() => ContentPackCodec.Parse(new byte[ContentPackCodec.MaxDescriptorBytes + 1]));
        string json = Descriptor().ToString(Formatting.None);
        Assert.Throws<InvalidDataException>(() => ContentPackCodec.Parse(Bytes(json.Insert(1, "\"SchemaVersion\":1,"))));
        Assert.Throws<InvalidDataException>(() => ContentPackCodec.Parse(Bytes(json + "{}")));
    }

    [Theory]
    [InlineData("asset-identity")]
    [InlineData("foreign-key")]
    [InlineData("duplicate-item")]
    [InlineData("foreign-icon")]
    [InlineData("missing-icon")]
    [InlineData("numeric-template")]
    [InlineData("unicode-description-bytes")]
    [InlineData("unknown-item-field")]
    [InlineData("null-description")]
    [InlineData("too-many-items")]
    [InlineData("oversized-items")]
    public void ComponentIdentityAndItemAuthorityStayBoundToTheDeclaringPack(string invalid)
    {
        var files = Files();
        var items = JObject.Parse(Encoding.UTF8.GetString(files["data/items.json"]));
        var item = (JObject)items["Items"][0];
        switch (invalid)
        {
            case "asset-identity":
                var index = JObject.Parse(Encoding.UTF8.GetString(files["assets/panel.index.json"]));
                index["PackId"] = "example:unrelated";
                files["assets/panel.index.json"] = JsonBytes(index);
                break;
            case "foreign-key": item["Key"] = "other:coin"; break;
            case "duplicate-item": ((JArray)items["Items"]).Add(item.DeepClone()); break;
            case "foreign-icon": item["IconAddress"] = "mods/other/icon"; break;
            case "missing-icon": item["IconAddress"] = "mods/example/missing"; break;
            case "numeric-template": item["BaseTemplate"] = "35"; break;
            case "unicode-description-bytes": item["Description"] = new string('\u00e9', 2049); break;
            case "unknown-item-field": item["Prefab"] = "anything"; break;
            case "null-description": item["Description"] = JValue.CreateNull(); break;
            case "too-many-items":
                items["Items"] = new JArray(Enumerable.Range(0, 128).Select(number => new JObject
                    { ["Key"] = "example:item" + number, ["BaseTemplate"] = "Coin", ["DisplayName"] = "Item" }));
                break;
        }
        files["data/items.json"] = invalid == "oversized-items" ? new byte[ContentPackCodec.MaxItemsBytes + 1] : JsonBytes(items);
        Assert.Throws<InvalidDataException>(() => Capture(Descriptor(), files));
    }

    [Fact]
    public void SourceAndSchemaValidationHappenBeforeAnExecutableInventoryIsPublished()
    {
        var files = Files();
        files["scripts/messages.json"] = Bytes("{\"Messages\":[],\"Unknown\":true}");
        files.Remove("scripts/main.lua");
        // A schema rejection occurs before attempting to read even the declared entrypoint.
        Assert.Throws<InvalidDataException>(() => Capture(Descriptor(), files));
        files = Files();
        files["scripts/main.lua"] = new byte[] { 0x1b, 0x4c, 0x75, 0x61 };
        Assert.Throws<InvalidDataException>(() => Capture(Descriptor(), files));
    }

    [Fact]
    public void ReservedSkinNamespaceIsLocalOnlyAndSingleComponentCanReuseWrapperIdentity()
    {
        var descriptor = new JObject
        {
            ["SchemaVersion"] = 1, ["PackId"] = "anomaly:skins", ["Version"] = "1.0.0",
            ["Assets"] = new JObject { ["PackId"] = "anomaly:skins", ["Index"] = "assets/skins.index.json" }
        };
        Assert.Equal("anomaly:skins", ContentPackCodec.Parse(JsonBytes(descriptor), remote: false).Assets.PackId);
        Assert.Throws<InvalidDataException>(() => ContentPackCodec.Parse(JsonBytes(descriptor), remote: true));
    }

    private static JObject Descriptor() => new JObject
    {
        ["SchemaVersion"] = 1, ["PackId"] = "example:panel", ["Version"] = "1.0.0",
        ["Scripts"] = new JObject { ["PackId"] = "example:panel_scripts", ["EntryPoint"] = "scripts/main.lua", ["Messages"] = "scripts/messages.json" },
        ["Assets"] = new JObject { ["PackId"] = "example:panel_assets", ["Index"] = "assets/panel.index.json" },
        ["Data"] = new JObject { ["Items"] = "data/items.json" }
    };
    private static Dictionary<string, byte[]> Files() => new Dictionary<string, byte[]>(StringComparer.Ordinal)
    {
        ["scripts/main.lua"] = Bytes("return 1"),
        ["scripts/messages.json"] = Bytes("{\"Messages\":[{\"Name\":\"State\",\"Direction\":\"ServerToClient\",\"Fields\":[]}]}"),
        ["assets/panel.index.json"] = JsonBytes(new JObject
        {
            ["SchemaVersion"] = AssetPackIndex.CurrentSchemaVersion, ["PackId"] = "example:panel_assets", ["Namespace"] = "example",
            ["UnityVersion"] = AssetPackIndex.SupportedUnityVersion, ["Platform"] = AssetPackIndex.SupportedPlatform,
            ["GameProfile"] = AssetPackIndex.SupportedGameProfile, ["BundleFile"] = "panel.bundle",
            ["BundleSha256"] = new string('a', 64), ["BundleBytes"] = 3, ["DecodedBytes"] = 1024,
            ["Assets"] = new JArray(new JObject
            {
                ["Address"] = "mods/example/icon", ["EntryName"] = "mods/example/icon", ["Kind"] = "Texture2D", ["Width"] = 16, ["Height"] = 16
            })
        }),
        ["data/items.json"] = Bytes("{\"SchemaVersion\":1,\"Items\":[{\"Key\":\"example:coin\",\"BaseTemplate\":\"Coin\",\"DisplayName\":\"Coin\",\"IconAddress\":\"mods/example/icon\"}]}")
    };
    private static CapturedContentPack Capture(JObject descriptor, Dictionary<string, byte[]> files)
        => ContentPackCodec.Capture(ContentPackCodec.Parse(JsonBytes(descriptor)), (path, _) => files[path]);
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static byte[] JsonBytes(JObject value) => Bytes(value.ToString(Formatting.None));
}
