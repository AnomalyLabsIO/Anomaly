using System.Reflection;
using System.Text;
using Anomaly.Server.Api.Content;
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
    public void DescriptorCapturesOnlyItsDeclaredEntryPointAndRetainsImmutableBytes()
    {
        WritePack("example:panel", "return true");
        WriteFile("example/panel/unlisted.lua", "\0");
        WriteFile("example/panel/nested/unlisted.index.json", "invalid");
        using var snapshot = AssetPackSnapshot.Build(_root);
        var pack = Assert.Single(snapshot.CopyScriptPacks());
        Assert.Equal("example:panel", pack.PackId);
        Assert.Equal("mods/example/panel/main.lua", Assert.Single(pack.Sources).Address);
        Assert.Empty(snapshot.CopyPacks());
        Assert.Empty(snapshot.Assets);
        Assert.Equal(2, snapshot.BuildManifest(1).Files.Count);
        var source = snapshot.Files.Values.Single(file => file.Kind == FileContentKind.LuaSource);
        WriteFile("example/panel/main.lua", "return false");
        Assert.Equal("return true", File.ReadAllText(source.FullPath));
        pack.Sources[0].Hash[0] ^= 0xff;
        Assert.NotEqual(pack.Sources[0].Hash, snapshot.CopyScriptPacks()[0].Sources[0].Hash);
        using var successor = AssetPackSnapshot.Build(_root);
        Assert.NotEqual(pack.IndexHash, successor.CopyScriptPacks()[0].IndexHash);
    }

    [Fact]
    public void LegacyFilesCannotReplaceAMissingPackDescriptor()
    {
        WriteFile("example/legacy/main.lua", "return true");
        WriteFile("example/legacy/content.index.json", "invalid");
        WriteFile("example/legacy/nested/pack.json", "invalid");
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
    }

    [Fact]
    public void EmptySourceHasAHashedLogicalEntryWithoutAZeroLengthTransfer()
    {
        WritePack("example:panel", "");
        using var snapshot = AssetPackSnapshot.Build(_root);
        var source = Assert.Single(Assert.Single(snapshot.CopyScriptPacks()).Sources);
        Assert.Equal(0U, source.Size);
        Assert.Equal(ScriptSourceValidator.EmptySha256Hex, ServerAssetManager.ToHex(source.Hash));
        Assert.Equal(FileContentKind.ScriptIndex, Assert.Single(snapshot.BuildManifest(1).Files).Kind);
    }

    [Fact]
    public void DirectoryEnumerationOrderDoesNotAffectGeneratedIndex()
    {
        WritePack("example:z", "local Z=1");
        WritePack("example:a", "local A=1");
        using var first = AssetPackSnapshot.Build(_root);
        using var second = AssetPackSnapshot.Build(_root);
        Assert.Equal(first.BuildManifest(1).ManifestSha256, second.BuildManifest(8).ManifestSha256);
        Assert.Equal(new[] { "example:a", "example:z" }, first.CopyScriptPacks().Select(pack => pack.PackId));
    }

    [Fact]
    public void SourceCountIsBoundedAcrossOwnerPacks()
    {
        for (int i = 0; i < ScriptPackLimits.MaxScripts; i++) WritePack("example:p" + i, "");
        using (var accepted = AssetPackSnapshot.Build(_root)) Assert.Equal(ScriptPackLimits.MaxScripts, accepted.CopyScriptPacks().Count);
        WritePack("example:overflow", "");
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
    }

    [Fact]
    public void SourceByteBudgetIncludesEachLogicalEntryEvenWhenContentIsDeduplicated()
    {
        string body = new string(' ', ScriptPackLimits.MaxScriptBytesPerFile);
        int count = ScriptPackLimits.MaxScriptBytes / ScriptPackLimits.MaxScriptBytesPerFile;
        for (int i = 0; i < count; i++) WritePack("example:p" + i, body);
        using (var accepted = AssetPackSnapshot.Build(_root)) Assert.Equal(count + 1, accepted.Files.Count);
        WritePack("example:overflow", " ");
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
    }

    [Theory]
    [InlineData("anomaly:panel", "return true")]
    [InlineData("example:panel", "\u0000")]
    [InlineData("example:panel", "\u001bLua")]
    public void InvalidDeclaredSourceFailsBeforePublication(string id, string source)
    {
        WritePack(id, source);
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
    }

    [Fact]
    public void InvalidUtf8AndOversizedSourcesAreRejected()
    {
        WritePack("example:panel", "");
        string source = Path.Combine(_root, "example", "panel", "main.lua");
        File.WriteAllBytes(source, new byte[] { 0xc3, 0x28 });
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
        File.WriteAllBytes(source, new byte[ScriptPackLimits.MaxScriptBytesPerFile + 1]);
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
    }

    [Fact]
    public void OwnerMessageDescriptorUsesStrictSchemaReader()
    {
        WritePack("example:panel", "", true);
        string messages = WriteFile("example/panel/messages.json", "{\"Messages\":[],\"Unexpected\":true}");
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
        File.WriteAllText(messages, "{\"Messages\":[]}");
        using var snapshot = AssetPackSnapshot.Build(_root);
        Assert.Single(snapshot.CopyScriptPacks());
    }

    [Fact]
    public void DirectoryIdentityIsExactAndNeverLowercased()
    {
        WriteFile("Example/panel/main.lua", "");
        WriteFile("Example/panel/pack.json", Descriptor("example:panel"));
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
    }

    [Theory]
    [InlineData("../outside.lua")]
    [InlineData("nested/../../outside.lua")]
    [InlineData("C:/outside.lua")]
    [InlineData("scripts\\main.lua")]
    public void UnsafeDeclaredPathsAreRejected(string relative)
    {
        Directory.CreateDirectory(_root);
        Assert.Throws<InvalidDataException>(() => ContentPackFiles.Resolve(_root, relative));
    }

    [Fact]
    public void EmbeddedPackUsesExactResourceNamesAndSurvivesDisposal()
    {
        IDisposable registration = Register();
        using var approved = AssetPackSnapshot.Build(null);
        var pack = Assert.Single(approved.CopyScriptPacks());
        Assert.Equal("mods/plugin/panel/main.lua", Assert.Single(pack.Sources).Address);
        Assert.True(approved.TryGetScriptSchema("plugin:panel", "State", Anomaly.Shared.Networking.AnomalyMessageDirection.ClientToServer, out _, out _));
        Assert.Throws<InvalidOperationException>(() => ContentPacks.RegisterEmbedded(typeof(ServerScriptSourceTests).Assembly, "Test.Panel"));
        registration.Dispose();
        using var future = AssetPackSnapshot.Build(null);
        Assert.Empty(future.CopyScriptPacks());
        Assert.True(approved.TryGetScriptSchema("plugin:panel", "State", Anomaly.Shared.Networking.AnomalyMessageDirection.ClientToServer, out _, out _));
        Assert.True(File.Exists(approved.Files.Values.First().FullPath));
    }

    [Fact]
    public void OwnerAndPluginLogicalPackConflictsRejectTheCandidate()
    {
        Register();
        WritePack("plugin:panel", "");
        Assert.Throws<InvalidDataException>(() => AssetPackSnapshot.Build(_root));
    }

    [Fact]
    public void MissingExactDescriptorAndDeclaredResourceDoNotRegisterAnything()
    {
        Assembly assembly = typeof(ServerScriptSourceTests).Assembly;
        long revision = ContentPacks.Revision;
        Assert.Throws<InvalidDataException>(() => ContentPacks.RegisterEmbedded(assembly, "Missing.Resources"));
        Assert.Throws<InvalidDataException>(() => ContentPacks.RegisterEmbedded(assembly, "Test.MissingSource"));
        Assert.Equal(revision, ContentPacks.Revision);
        using var empty = AssetPackSnapshot.Build(null);
        Assert.Empty(empty.CopyScriptPacks());
    }

    [Fact]
    public void FailedRescanKeepsPreviousPublishedSourceInventory()
    {
        WritePack("example:panel", "return 1");
        ServerAssetManager.Init(_root);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        byte[] initial;
        using (var snapshot = ServerAssetManager.AcquireSnapshot()) initial = Assert.Single(snapshot.CopyScriptPacks()).IndexHash;
        WritePack("example:panel", "\0");
        ClearWarnings();
        ServerAssetManager.Rescan();
        WaitForFailedScan();
        using var retained = ServerAssetManager.AcquireSnapshot();
        Assert.Equal(initial, Assert.Single(retained.CopyScriptPacks()).IndexHash);
        Assert.True(ServerAssetManager.IsReady);
    }

    [Fact]
    public void InitialInvalidSourceCannotPublishAnEmptyReadyOffer()
    {
        WritePack("example:panel", "\0");
        ClearWarnings();
        ServerAssetManager.Init(_root);
        WaitForFailedScan();
        Assert.False(ServerAssetManager.IsReady);
        Assert.False(ServerAssetManager.TryAcquireOfferSnapshot(out _));
    }

    [Fact]
    public void RegistrationRemovalBlocksNewOffersUntilItsNewInventoryCanPublish()
    {
        IDisposable registration = Register();
        WritePack("example:panel", "return 1");
        ServerAssetManager.Init(_root);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        using var approved = ServerAssetManager.AcquireSnapshot();
        Assert.Equal(2, approved.CopyScriptPacks().Count);
        WritePack("example:panel", "\0");
        ClearWarnings();
        registration.Dispose();
        WaitForFailedScan();
        Assert.False(ServerAssetManager.IsReady);
        Assert.False(ServerAssetManager.TryAcquireOfferSnapshot(out _));
        Assert.Equal(2, approved.CopyScriptPacks().Count);
        WritePack("example:panel", "return 2");
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
        Register();
        if (ServerAssetManager.TryAcquireOfferSnapshot(out var racing))
            using (racing) Assert.Equal("plugin:panel", Assert.Single(racing.CopyScriptPacks()).PackId);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        Assert.True(ServerAssetManager.TryAcquireOfferSnapshot(out var future));
        using (future) Assert.Equal("plugin:panel", Assert.Single(future.CopyScriptPacks()).PackId);
    }

    private static void ClearWarnings() { while (Anomaly.Server.Utils.OffThreadLogger.Warnings.TryDequeue(out _)) { } }
    private static void WaitForFailedScan() => Assert.True(SpinWait.SpinUntil(() =>
        Anomaly.Server.Utils.OffThreadLogger.Warnings.Any(value => value.Contains("retaining previous generation")), 5000));
    private IDisposable Register()
    {
        var registration = ContentPacks.RegisterEmbedded(typeof(ServerScriptSourceTests).Assembly, "Test.Panel/");
        _registrations.Add(registration);
        return registration;
    }
    private void WritePack(string id, string body, bool messages = false)
    {
        string directory = id.Replace(':', '/');
        WriteFile(directory + "/main.lua", body);
        if (messages) WriteFile(directory + "/messages.json", "{\"Messages\":[]}");
        WriteFile(directory + "/pack.json", Descriptor(id, messages));
    }
    private static string Descriptor(string id, bool messages = false) =>
        "{\"SchemaVersion\":1,\"PackId\":\"" + id + "\",\"Version\":\"1.0.0\",\"Scripts\":{\"PackId\":\"" + id + "\",\"EntryPoint\":\"main.lua\"" +
        (messages ? ",\"Messages\":\"messages.json\"" : "") + "}}";
    private string WriteFile(string relative, string body)
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
        string spool = Path.Combine(_root, ".anomaly-spool");
        Assert.True(SpinWait.SpinUntil(() => !Directory.Exists(spool) || !Directory.EnumerateDirectories(spool).Any(), 5000));
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
