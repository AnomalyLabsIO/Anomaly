using Anomaly.Client.Content;
using Anomaly.Shared.Content;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

[Collection("Script pack snapshots")]
public sealed class ScriptPackManagerTests : IDisposable
{
    [Fact]
    public void VerifiedSnapshotsTransferOwnershipOnlyOnce()
    {
        ScriptPackManager.PrepareRemote(EmptyCatalog());
        Assert.Empty(ScriptPackManager.TakeRemoteScripts());
        Assert.Throws<InvalidOperationException>(() => ScriptPackManager.TakeRemoteScripts());
    }

    [Fact]
    public void ResetDropsPreparedSnapshots()
    {
        ScriptPackManager.PrepareRemote(EmptyCatalog());
        ScriptPackManager.Reset();
        Assert.Throws<InvalidOperationException>(() => ScriptPackManager.TakeRemoteScripts());
    }

    [Fact]
    public void FailedPreparationCannotExposeThePrecedingOffer()
    {
        ScriptPackManager.PrepareRemote(EmptyCatalog());
        Assert.Throws<InvalidDataException>(() => ScriptPackManager.PrepareRemote(null!));
        Assert.Throws<InvalidOperationException>(() => ScriptPackManager.TakeRemoteScripts());
    }

    private static ContentCatalog EmptyCatalog() => CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 1, 0, new byte[32]);
    public void Dispose() => ScriptPackManager.Reset();
}

[CollectionDefinition("Script pack snapshots", DisableParallelization = true)]
public sealed class ScriptPackSnapshotCollection { }
