using System.Security.Cryptography;
using System.Text;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class ContentPackOrderingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "anomaly-component-order-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void IndependentComponentIdsProduceCanonicalAuthenticatedOffers()
    {
        WritePack("a", "z", new byte[] { 1, 2, 3 });
        WritePack("b", "y", new byte[] { 4, 5, 6 });
        using var snapshot = AssetPackSnapshot.Build(_root);
        var catalog = new ContentCatalog
        {
            ManifestSha256 = snapshot.BuildManifest(1).ManifestSha256,
            Packs = snapshot.CopyPacks(), ScriptPacks = snapshot.CopyScriptPacks()
        };
        Assert.Equal(new[] { "example:y_assets", "example:z_assets" }, catalog.Packs.Select(pack => pack.PackId));
        Assert.Equal(new[] { "example:y_scripts", "example:z_scripts" }, catalog.ScriptPacks.Select(pack => pack.PackId));
        catalog.CatalogSha256 = CatalogBuilder.ComputeCatalogHash(catalog);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            new ContentCatalogMessage { Catalog = catalog }.Serialize(writer);
        stream.Position = 0;
        var received = new ContentCatalogMessage();
        using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true)) received.Deserialize(reader);
        Assert.Equal(catalog.CatalogSha256, received.Catalog.CatalogSha256);
        Assert.Equal("mods/example/b/main.lua", received.Catalog.ScriptPacks[0].Sources[0].Address);
    }

    private void WritePack(string wrapper, string component, byte[] bundle)
    {
        string pack = Path.Combine(_root, "example", wrapper);
        Directory.CreateDirectory(pack);
        File.WriteAllText(Path.Combine(pack, "main.lua"), "return 1", new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(pack, "content.bundle"), bundle);
        string hex;
        using (var sha = SHA256.Create()) hex = BitConverter.ToString(sha.ComputeHash(bundle)).Replace("-", "").ToLowerInvariant();
        File.WriteAllText(Path.Combine(pack, "content.index.json"),
            "{\"SchemaVersion\":2,\"PackId\":\"example:" + component + "_assets\",\"Namespace\":\"example\"," +
            "\"UnityVersion\":\"6000.0.43f1\",\"Platform\":\"windows-x64\",\"GameProfile\":\"scpsl-14.2.7-unity-6000.0.43f1-windows-x64-v1\"," +
            "\"BundleFile\":\"content.bundle\",\"BundleSha256\":\"" + hex + "\",\"BundleBytes\":3,\"DecodedBytes\":1024,\"Assets\":[{" +
            "\"Address\":\"mods/example/" + wrapper + "/icon\",\"EntryName\":\"mods/example/" + wrapper + "/icon\",\"Kind\":\"Texture2D\",\"Width\":16,\"Height\":16}]}", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(pack, "pack.json"),
            "{\"SchemaVersion\":1,\"PackId\":\"example:" + wrapper + "\",\"Version\":\"1.0.0\"," +
            "\"Scripts\":{\"PackId\":\"example:" + component + "_scripts\",\"EntryPoint\":\"main.lua\"}," +
            "\"Assets\":{\"PackId\":\"example:" + component + "_assets\",\"Index\":\"content.index.json\"}}", new UTF8Encoding(false));
    }

    public void Dispose()
    {
        ServerAssetManager.Reset();
        string spool = Path.Combine(_root, ".anomaly-spool");
        Assert.True(SpinWait.SpinUntil(() => !Directory.Exists(spool) || !Directory.EnumerateDirectories(spool).Any(), 5000));
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
