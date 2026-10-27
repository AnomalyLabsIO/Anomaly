using System.Text;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Utils;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class ContentPackPublicationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "anomaly-pack-publication-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FrozenDefinitionRefreshFailureRetainsThePriorPublishableSnapshot()
    {
        WritePack("Original");
        ServerAssetManager.Init(_root);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        using var approved = ServerAssetManager.AcquireSnapshot();
        ContentRegistry.FreezeDefinitions(approved);
        WritePack("Changed");
        ClearWarnings();
        ServerAssetManager.Rescan();
        WaitForFailure();
        Assert.True(ServerAssetManager.TryAcquireOfferSnapshot(out var retained));
        using (retained) Assert.Equal("Original", Assert.Single(retained.Items).DisplayName);
        Assert.Equal("Original", Assert.Single(approved.Items).DisplayName);
    }

    [Fact]
    public void RetainedOldOfferCannotFreezeAfterDifferentDefinitionsHavePublished()
    {
        WritePack("Original");
        ServerAssetManager.Init(_root);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        using var old = ServerAssetManager.AcquireSnapshot();
        WritePack("Changed");
        ServerAssetManager.Rescan();
        Assert.True(SpinWait.SpinUntil(() =>
        {
            using var current = ServerAssetManager.AcquireSnapshot();
            return current.Items.Length == 1 && current.Items[0].DisplayName == "Changed";
        }, 5000));
        Assert.Throws<InvalidOperationException>(() => ContentRegistry.FreezeDefinitions(old));
        Assert.False(ContentRegistry.Frozen);
        using var latest = ServerAssetManager.AcquireSnapshot();
        ContentRegistry.FreezeDefinitions(latest);
        Assert.True(ContentRegistry.Frozen);
    }

    [Fact]
    public void FreezeAndCandidatePublicationUseTheSameGate()
    {
        WritePack("Original");
        ServerAssetManager.Init(_root);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        using var old = ServerAssetManager.AcquireSnapshot();
        lock (ServerAssetManager.Sync)
        {
            WritePack("Changed");
            ClearWarnings();
            ServerAssetManager.Rescan();
            ContentRegistry.FreezeDefinitions(old);
        }
        WaitForFailure();
        using var retained = ServerAssetManager.AcquireSnapshot();
        Assert.Equal("Original", Assert.Single(retained.Items).DisplayName);
        Assert.True(ServerAssetManager.IsReady);
    }

    [Fact]
    public void UnsupportedTemplateRefreshCannotReplaceAValidInventory()
    {
        WritePack("Original");
        ServerAssetManager.Init(_root);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        WritePack("Changed", "NotAVanillaItem");
        ClearWarnings();
        ServerAssetManager.Rescan();
        WaitForFailure();
        using var retained = ServerAssetManager.AcquireSnapshot();
        Assert.Equal("Coin", Assert.Single(retained.Items).BaseTemplate);
        Assert.Equal("Original", Assert.Single(retained.Items).DisplayName);
        Assert.False(ContentRegistry.Frozen);
    }

    private void WritePack(string name, string template = "Coin")
    {
        string pack = Path.Combine(_root, "example", "items");
        Directory.CreateDirectory(pack);
        File.WriteAllText(Path.Combine(pack, "pack.json"), "{\"SchemaVersion\":1,\"PackId\":\"example:items\",\"Version\":\"1.0.0\",\"Data\":{\"Items\":\"items.json\"}}", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(pack, "items.json"), "{\"SchemaVersion\":1,\"Items\":[{\"Key\":\"example:coin\",\"BaseTemplate\":\"" + template + "\",\"DisplayName\":\"" + name + "\"}]}", new UTF8Encoding(false));
    }
    private static void ClearWarnings() { while (OffThreadLogger.Warnings.TryDequeue(out _)) { } }
    private static void WaitForFailure() => Assert.True(SpinWait.SpinUntil(() => OffThreadLogger.Warnings.Any(value => value.Contains("retaining previous generation")), 5000));
    public void Dispose()
    {
        ServerAssetManager.Reset();
        ContentRegistry.ResetDefinitions();
        string spool = Path.Combine(_root, ".anomaly-spool");
        Assert.True(SpinWait.SpinUntil(() => !Directory.Exists(spool) || !Directory.EnumerateDirectories(spool).Any(), 5000));
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
