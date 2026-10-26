using System.Reflection;
using System.Text;
using Anomaly.Server.Api.Scripting;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Scripting;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class ServerScriptSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "anomaly-script-sources-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _registrations = new();

    [Fact]
    public void OwnerSourcesGenerateDeterministicIndexesAndImmutableDeduplicatedFiles()
    {
        WriteSource("example/panel/Main.lua", "return true");
        WriteSource("example/panel/sub/Second.lua", "return true");
        using var snapshot = AssetPackSnapshot.Build(null, _root);
        var pack = Assert.Single(snapshot.CopyScriptPacks());
        Assert.Equal("example:panel", pack.PackId);
        Assert.Equal(new[] { "mods/example/panel/main.lua", "mods/example/panel/sub/second.lua" }, pack.Sources.Select(source => source.Address));
        Assert.Empty(snapshot.CopyPacks());
        Assert.Empty(snapshot.Assets);
        Assert.Equal(2, snapshot.BuildManifest(1).Files.Count);
        Assert.Single(snapshot.Files.Values.Where(file => file.Kind == FileContentKind.LuaSource));
        var source = snapshot.Files.Values.Single(file => file.Kind == FileContentKind.LuaSource);
        File.WriteAllText(Path.Combine(_root, "example", "panel", "Main.lua"), "return false");
        Assert.Equal("return true", File.ReadAllText(source.FullPath));
        pack.Sources[0].Hash[0] ^= 0xff;
        Assert.NotEqual(pack.Sources[0].Hash, snapshot.CopyScriptPacks()[0].Sources[0].Hash);
        using var successor = AssetPackSnapshot.Build(null, _root);
        Assert.NotEqual(pack.IndexHash, successor.CopyScriptPacks()[0].IndexHash);
    }

    [Fact]
    public void EmptySourceHasAHashedLogicalEntryWithoutAZeroLengthTransfer()
    {
        WriteSource("example/panel/empty.lua", "");
        using var snapshot = AssetPackSnapshot.Build(null, _root);
        var source = Assert.Single(Assert.Single(snapshot.CopyScriptPacks()).Sources);
        Assert.Equal(0U, source.Size);
        Assert.Equal(ScriptSourceValidator.EmptySha256Hex, ServerAssetManager.ToHex(source.Hash));
        Assert.Equal(FileContentKind.ScriptIndex, Assert.Single(snapshot.BuildManifest(1).Files).Kind);
    }

    [Fact]
    public void DirectoryEnumerationOrderDoesNotAffectGeneratedIndex()
    {
        WriteSource("example/panel/z.lua", "local Z=1");
        WriteSource("example/panel/a.lua", "local A=1");
        using var first = AssetPackSnapshot.Build(null, _root);
        using var second = AssetPackSnapshot.Build(null, _root);
        Assert.Equal(Assert.Single(first.CopyScriptPacks()).IndexHash, Assert.Single(second.CopyScriptPacks()).IndexHash);
        Assert.Equal(first.BuildManifest(1).ManifestSha256, second.BuildManifest(8).ManifestSha256);
    }

    [Fact]
    public void SourceCountAndBytesAreBoundedAcrossOwnerPacks()
    {
        for (int i = 0; i < 8; i++) WriteSource("example/p" + i + "/main.lua", "");
        using (var accepted = AssetPackSnapshot.Build(null, _root)) Assert.Equal(8, accepted.CopyScriptPacks().Count);
        WriteSource("example/overflow/main.lua", "");
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(null, _root));
    }

    [Fact]
    public void SourceByteBudgetIncludesEachLogicalEntryEvenWhenContentIsDeduplicated()
    {
        string body = new string(' ', ScriptPackLimits.MaxScriptBytesPerFile);
        for (int i = 0; i < 4; i++) WriteSource("example/panel/" + i + ".lua", body);
        using (var accepted = AssetPackSnapshot.Build(null, _root)) Assert.Equal(2, accepted.Files.Count);
        WriteSource("example/panel/overflow.lua", " ");
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(null, _root));
    }

    [Theory]
    [InlineData("bad.lua", "return true")]
    [InlineData("anomaly/panel/start.lua", "return true")]
    [InlineData("example/panel/start.lua", "\u0000")]
    [InlineData("example/panel/start.lua", "\u001bLua")]
    public void InvalidOwnerSourceFailsBeforePublication(string path, string source)
    {
        WriteSource(path, source);
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(null, _root));
    }

    [Fact]
    public void InvalidUtf8AndOversizedSourcesAreRejected()
    {
        string source = WriteSource("example/panel/start.lua", "");
        File.WriteAllBytes(source, new byte[] { 0xc3, 0x28 });
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(null, _root));
        File.WriteAllBytes(source, new byte[ScriptPackLimits.MaxScriptBytesPerFile + 1]);
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(null, _root));
    }

    [Fact]
    public void OwnerMessageDescriptorUsesStrictSchemaReader()
    {
        WriteSource("example/panel/start.lua", "");
        string descriptor = WriteSource("example/panel/messages.json", "{\"Messages\":[],\"Unexpected\":true}");
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(null, _root));
        File.WriteAllText(descriptor, "{\"Messages\":[]}");
        using var snapshot = AssetPackSnapshot.Build(null, _root);
        Assert.Single(snapshot.CopyScriptPacks());
    }

    [Fact]
    public void EmbeddedPluginSourcesAreExplicitlyRegisteredCapturedAndDisposable()
    {
        IDisposable registration = Register("plugin:panel", "Anomaly.Server.Tests.ScriptResources", "Anomaly.Server.Tests.ScriptResources.Messages.json");
        using var approved = AssetPackSnapshot.Build(null);
        var pack = Assert.Single(approved.CopyScriptPacks());
        Assert.Equal("mods/plugin/panel/main.lua", Assert.Single(pack.Sources).Address);
        Assert.True(approved.TryGetScriptSchema("plugin:panel", "State", Anomaly.Shared.Networking.AnomalyMessageDirection.ClientToServer, out _, out _));
        Assert.Throws<InvalidOperationException>(() => ClientScripts.RegisterEmbedded("plugin:panel", typeof(ServerScriptSourceTests).Assembly, "Anomaly.Server.Tests.ScriptResources"));
        registration.Dispose();
        using var future = AssetPackSnapshot.Build(null);
        Assert.Empty(future.CopyScriptPacks());
        Assert.True(approved.TryGetScriptSchema("plugin:panel", "State", Anomaly.Shared.Networking.AnomalyMessageDirection.ClientToServer, out _, out _));
        Assert.True(File.Exists(approved.Files.Values.First().FullPath));
    }

    [Fact]
    public void OwnerAndPluginPackConflictsRejectTheCandidate()
    {
        Register("plugin:panel", "Anomaly.Server.Tests.ScriptResources");
        WriteSource("plugin/panel/owner.lua", "");
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(null, _root));
    }

    [Fact]
    public void MissingEmbeddedPrefixAndSchemaResourceDoNotCreateRegistrations()
    {
        Assembly assembly = typeof(ServerScriptSourceTests).Assembly;
        Assert.Throws<InvalidDataException>(() => ClientScripts.RegisterEmbedded("plugin:panel", assembly, "Missing.Resources"));
        Assert.Throws<InvalidDataException>(() => ClientScripts.RegisterEmbedded("plugin:panel", assembly, "Anomaly.Server.Tests.ScriptResources", "Missing.Messages.json"));
        using var empty = AssetPackSnapshot.Build(null);
        Assert.Empty(empty.CopyScriptPacks());
    }

    [Fact]
    public void NestedDescriptorAndExcessiveDirectoryDepthAreRejected()
    {
        WriteSource("example/panel/start.lua", "");
        string nested = WriteSource("example/panel/nested/messages.json", "{\"Messages\":[]}");
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(null, _root));
        File.Delete(nested);
        string relative = "example/panel/" + string.Join("/", Enumerable.Repeat("nested", 15)) + "/main.lua";
        WriteSource(relative, "");
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(null, _root));
    }

    [Fact]
    public void FailedRescanKeepsPreviousPublishedSourceInventory()
    {
        WriteSource("example/panel/main.lua", "return 1");
        ServerAssetManager.Init(null, _root);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        byte[] initial;
        using (var snapshot = ServerAssetManager.AcquireSnapshot()) initial = Assert.Single(snapshot.CopyScriptPacks()).IndexHash;
        WriteSource("example/panel/main.lua", "\0");
        while (Anomaly.Server.Utils.OffThreadLogger.Warnings.TryDequeue(out _)) { }
        ServerAssetManager.Rescan();
        Assert.True(SpinWait.SpinUntil(() => Anomaly.Server.Utils.OffThreadLogger.Warnings.Any(value => value.Contains("retaining previous generation")), 5000));
        using var retained = ServerAssetManager.AcquireSnapshot();
        Assert.Equal(initial, Assert.Single(retained.CopyScriptPacks()).IndexHash);
        Assert.True(ServerAssetManager.IsReady);
    }

    [Fact]
    public void InitialInvalidSourceCannotPublishAnEmptyReadyOffer()
    {
        WriteSource("example/panel/main.lua", "\0");
        while (Anomaly.Server.Utils.OffThreadLogger.Warnings.TryDequeue(out _)) { }
        ServerAssetManager.Init(null, _root);
        Assert.True(SpinWait.SpinUntil(() => Anomaly.Server.Utils.OffThreadLogger.Warnings.Any(value => value.Contains("retaining previous generation")), 5000));
        Assert.False(ServerAssetManager.IsReady);
        Assert.False(ServerAssetManager.TryAcquireOfferSnapshot(out _));
    }

    [Fact]
    public void RegistrationRemovalBlocksNewOffersUntilItsNewInventoryCanPublish()
    {
        IDisposable registration = Register("plugin:panel", "Anomaly.Server.Tests.ScriptResources");
        WriteSource("example/panel/main.lua", "return 1");
        ServerAssetManager.Init(null, _root);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        using var approved = ServerAssetManager.AcquireSnapshot();
        Assert.Equal(2, approved.CopyScriptPacks().Count);
        WriteSource("example/panel/main.lua", "\0");
        while (Anomaly.Server.Utils.OffThreadLogger.Warnings.TryDequeue(out _)) { }
        registration.Dispose();
        Assert.True(SpinWait.SpinUntil(() => Anomaly.Server.Utils.OffThreadLogger.Warnings.Any(value => value.Contains("retaining previous generation")), 5000));
        Assert.False(ServerAssetManager.IsReady);
        Assert.False(ServerAssetManager.TryAcquireOfferSnapshot(out _));
        Assert.Equal(2, approved.CopyScriptPacks().Count);
        WriteSource("example/panel/main.lua", "return 2");
        ServerAssetManager.Rescan();
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        Assert.True(ServerAssetManager.TryAcquireOfferSnapshot(out var future));
        using (future) Assert.Equal("example:panel", Assert.Single(future.CopyScriptPacks()).PackId);
    }

    [Fact]
    public void RegistrationAdditionCannotBeOmittedFromANewOffer()
    {
        ServerAssetManager.Init(null);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        Register("plugin:panel", "Anomaly.Server.Tests.ScriptResources");
        if (ServerAssetManager.TryAcquireOfferSnapshot(out var racing))
            using (racing) Assert.Equal("plugin:panel", Assert.Single(racing.CopyScriptPacks()).PackId);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        Assert.True(ServerAssetManager.TryAcquireOfferSnapshot(out var future));
        using (future) Assert.Equal("plugin:panel", Assert.Single(future.CopyScriptPacks()).PackId);
    }

    private IDisposable Register(string packId, string prefix, string descriptor = null)
    {
        var registration = ClientScripts.RegisterEmbedded(packId, typeof(ServerScriptSourceTests).Assembly, prefix, descriptor);
        _registrations.Add(registration);
        return registration;
    }

    private string WriteSource(string relative, string body)
    {
        string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, body, new UTF8Encoding(false));
        return path;
    }

    public void Dispose()
    {
        foreach (IDisposable registration in _registrations) registration.Dispose();
        ServerAssetManager.Reset();
        string absolute = Path.GetFullPath(_root);
        string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("anomaly-script-sources-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected fixture path.");
        string spool = Path.Combine(absolute, ".anomaly-spool");
        Assert.True(SpinWait.SpinUntil(() => !Directory.Exists(spool) || !Directory.EnumerateDirectories(spool).Any(), 5000));
        if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
    }
}
