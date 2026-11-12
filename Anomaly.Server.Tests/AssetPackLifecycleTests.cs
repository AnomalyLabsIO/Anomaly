using System.Security.Cryptography;
using System.Text;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Assets;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class AssetPackLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "anomaly-pack-tests", Guid.NewGuid().ToString("N"));
    private static readonly AssetAddress Address = AssetAddress.Parse("mods/fixture/textures/checker");
    private string PackRoot => Path.Combine(_root, "fixture", "pack");

    [Fact]
    public void Published_bytes_and_metadata_survive_source_replacement_until_last_lease_is_released()
    {
        byte[] original = Enumerable.Range(0, 100000).Select(index => (byte)(index % 251)).ToArray();
        WritePack(original);
        var first = AssetPackSnapshot.Build(_root);
        var lease = first.Retain();
        string spoolFile = first.Files.Values.Single(file => file.Kind == FileContentKind.Bundle).FullPath;
        var firstOffer = first.CopyPacks().Single();
        first.Dispose();
        WritePack(new byte[] { 7, 8, 9 });
        using (var second = AssetPackSnapshot.Build(_root))
        {
            Assert.Equal(original, File.ReadAllBytes(spoolFile));
            Assert.NotEqual(firstOffer.BundleHash, second.CopyPacks().Single().BundleHash);
            Assert.True(lease.Contains(Address, AssetKind.Texture2D));
            Assert.Equal(2, lease.BuildManifest(4).Files.Count);
        }
        lease.Dispose();
        Assert.True(SpinWait.SpinUntil(() => !File.Exists(spoolFile), 5000));
    }

    [Fact]
    public void Incomplete_or_conflicting_pairs_are_rejected_before_publication()
    {
        WritePack(new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(PackRoot, "content.bundle"), new byte[] { 4, 5, 6 });
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
        WritePack(new byte[] { 1, 2, 3 });
        string original = File.ReadAllText(Path.Combine(PackRoot, "content.index.json"));
        File.WriteAllText(Path.Combine(PackRoot, "content.index.json"), original.Replace("fixture:pack", "fixture:second"));
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
    }

    [Fact]
    public void Transfer_reader_uses_retained_spool_and_reads_ordered_chunks_after_original_is_removed()
    {
        byte[] bytes = Enumerable.Range(0, 200000).Select(index => (byte)(index % 253)).ToArray();
        WritePack(bytes);
        var snapshot = AssetPackSnapshot.Build(_root);
        var file = snapshot.Files.Values.Single(value => value.Kind == FileContentKind.Bundle);
        using (var reader = new AssetChunkReader(snapshot, file))
        {
            snapshot.Dispose();
            File.Delete(Path.Combine(PackRoot, "content.bundle"));
            using (var result = new MemoryStream())
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!reader.IsCompleted && DateTime.UtcNow < deadline)
                {
                    if (reader.TryRead(out byte[] chunk)) result.Write(chunk, 0, chunk.Length);
                    else Thread.Sleep(1);
                }
                Assert.True(reader.IsCompleted);
                Assert.Null(reader.Error);
                Assert.Equal(bytes, result.ToArray());
            }
        }
        Assert.True(SpinWait.SpinUntil(() => !File.Exists(file.FullPath), 5000));
    }

    [Fact]
    public void Cancellation_releases_a_worker_blocked_on_its_bounded_chunk_queue()
    {
        WritePack(new byte[1024 * 1024]);
        var snapshot = AssetPackSnapshot.Build(_root);
        var file = snapshot.Files.Values.Single(value => value.Kind == FileContentKind.Bundle);
        var reader = new AssetChunkReader(snapshot, file);
        snapshot.Dispose();
        reader.Dispose();
        Assert.True(SpinWait.SpinUntil(() => !File.Exists(file.FullPath), 5000));
    }

    [Fact]
    public void EmbeddedBundleCaptureSurvivesRegistrationDisposalAndMissingBundleIsAtomic()
    {
        long revision = Anomaly.Server.Api.Content.ContentPacks.Revision;
        Assert.Throws<InvalidDataException>(() => Anomaly.Server.Api.Content.ContentPacks.RegisterEmbedded(
            typeof(AssetPackLifecycleTests).Assembly, "Test.MissingBundle"));
        Assert.Equal(revision, Anomaly.Server.Api.Content.ContentPacks.Revision);
        using var registration = Anomaly.Server.Api.Content.ContentPacks.RegisterEmbedded(
            typeof(AssetPackLifecycleTests).Assembly, "Test.Visual");
        using var snapshot = AssetPackSnapshot.Build(null);
        var file = Assert.Single(snapshot.Files.Values.Where(value => value.Kind == FileContentKind.Bundle));
        registration.Dispose();
        using var future = AssetPackSnapshot.Build(null);
        Assert.Empty(future.CopyPacks());
        Assert.True(snapshot.Contains(AssetAddress.Parse("mods/embedded/icon"), AssetKind.Texture2D));
        Assert.Equal("embedded bundle", File.ReadAllText(file.FullPath));
        Assert.Equal(15U, file.Size);
    }

    private void WritePack(byte[] bundle)
    {
        Directory.CreateDirectory(PackRoot);
        File.WriteAllText(Path.Combine(PackRoot, "pack.json"), "{\"SchemaVersion\":1,\"PackId\":\"fixture:pack\",\"Version\":\"1.0.0\",\"Assets\":{\"PackId\":\"fixture:pack\",\"Index\":\"content.index.json\"}}", new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(PackRoot, "content.bundle"), bundle);
        string hash;
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bundle)).Replace("-", "").ToLowerInvariant();
        string json = "{\"SchemaVersion\":2,\"PackId\":\"fixture:pack\",\"Namespace\":\"fixture\",\"UnityVersion\":\"6000.0.43f1\",\"Platform\":\"windows-x64\",\"GameProfile\":\"" + AssetPackIndex.SupportedGameProfile + "\",\"BundleFile\":\"content.bundle\",\"BundleSha256\":\"" + hash + "\",\"BundleBytes\":" + bundle.Length + ",\"DecodedBytes\":16,\"Assets\":[{\"Address\":\"" + Address.Value + "\",\"EntryName\":\"" + Address.Value + "\",\"Kind\":\"Texture2D\",\"Width\":2,\"Height\":2}],\"Visuals\":[]}";
        File.WriteAllText(Path.Combine(PackRoot, "content.index.json"), json, new UTF8Encoding(false));
    }
    public void Dispose()
    {
        string spool = Path.Combine(_root, ".anomaly-spool");
        Assert.True(SpinWait.SpinUntil(() => !Directory.Exists(spool) || !Directory.EnumerateDirectories(spool).Any(), 5000));
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
