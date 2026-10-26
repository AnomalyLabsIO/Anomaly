using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Assets;
using Anomaly.Client.Components;
using Anomaly.Client.Content;
using Anomaly.Client.FileTransfer;
using Anomaly.Client.Internal.Bootstrap;
using Anomaly.Client.Internal.Map;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Il2CppMirror;
using MelonLoader;
using Xunit;

namespace Anomaly.Shared.Tests;

// Tests the real consent/connection coordinator; source verification and VM execution
// remain explicit injected boundaries here and have their own production suites.
public sealed class ScriptPackConsentTests : IDisposable
{
    public ScriptPackConsentTests()
    {
        ContentCatalogClient.Init();
        ContentConsentFixture.Reset();
        ContentConsentFixture.Begin();
        FileCache.HasFileResult = _ => true;
    }

    [Fact]
    public void CachedAssetApprovalStillRequiresSeparateExecutionApproval()
    {
        var offer = ContentConsentFixture.CreateOffer(100, scriptCount: 1);
        ContentConsentStore.TryRemember(Anomaly.Client.Auth.AuthHandler.CurrentServerFingerprint,
            offer.Catalog.CatalogSha256, offer.Catalog.ManifestSha256);
        ContentConsentFixture.Deliver(offer);
        Assert.Equal(new[] { "script-execution" }, PasswordPrompt.Owners);
        Assert.Equal(0, ScriptingBootstrap.PrepareCalls);
        Assert.False(ContentCatalogClient.IsActivated);
        Assert.Empty(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>());
        Assert.Single(AnomalyMessaging.Sent.OfType<FileRequestMessage>());
    }

    [Fact]
    public void SecondApprovalPreparesWithinCurrentScopeBeforeActivatedAcknowledgement()
    {
        var offer = ContentConsentFixture.CreateOffer(101, scriptCount: 1);
        ContentConsentFixture.Deliver(offer);
        Assert.Equal("content-catalog", PasswordPrompt.Owner);
        PasswordPrompt.Approve();
        Assert.Equal("script-execution", PasswordPrompt.Owner);
        Assert.Equal(0, ScriptingBootstrap.PrepareCalls);
        Assert.Equal(0, ClientContentRegistrar.Activations);
        Assert.Empty(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>());
        bool observed = false;
        ScriptingBootstrap.ObserveExecute = (catalog, approved) =>
        {
            observed = true;
            Assert.True(approved);
            Assert.Same(offer.Catalog, catalog);
            Assert.True(ConnectionContentBootstrap.IsCurrentScriptScope);
            Assert.Empty(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>());
            Assert.Equal(0, ClientRuntime.ReadyPublications);
            Assert.Equal(0, NetworkClient.AddPlayerCalls);
        };
        Action approveScripts = PasswordPrompt.Approve;
        approveScripts();
        approveScripts(); // A repeated UI callback cannot initialize twice.
        Assert.True(observed);
        Assert.Equal(1, ScriptingBootstrap.PrepareCalls);
        Assert.Equal(new[] { true }, ScriptingBootstrap.PrepareApprovals);
        Assert.Equal(1, ScriptingBootstrap.PreparedScriptCount);
        Assert.True(ContentCatalogClient.IsActivated);
        Assert.Equal(ContentCatalogActivationStatus.Activated,
            Assert.Single(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>()).Status);
        Assert.Equal(0, ClientRuntime.ReadyPublications);
    }

    [Fact]
    public void DecliningExecutionNeverPreparesAndClosesTheSameConnection()
    {
        var connection = NetworkClient.connection;
        ContentConsentFixture.Deliver(ContentConsentFixture.CreateOffer(102, scriptCount: 1));
        PasswordPrompt.Approve();
        PasswordPrompt.Decline();
        Assert.Equal(0, ScriptingBootstrap.PrepareCalls);
        Assert.False(ContentCatalogClient.IsActivated);
        Assert.Equal(ContentCatalogActivationStatus.Failed,
            Assert.Single(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>()).Status);
        ContentConsentFixture.DrainGrace();
        Assert.Equal(1, connection.Disconnects);
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PriorExecutionApprovalCannotRunAfterResetOrReconnect(bool reconnect)
    {
        ContentConsentFixture.Deliver(ContentConsentFixture.CreateOffer(103, scriptCount: 1));
        PasswordPrompt.Approve();
        Action staleApproval = PasswordPrompt.Approve;
        if (reconnect)
        {
            AnomalyMessaging.ClearTransportSession();
            NetworkClient.connection = new NetworkConnection { Pointer = new IntPtr(99) };
            ConnectionContentBootstrap.BeginConnection();
        }
        else ConnectionContentBootstrap.BeginFacilityGeneration(2);
        // The identical catalog may be offered in a new random manifest session;
        // cached asset consent still cannot revive the earlier execution callback.
        ContentConsentFixture.Deliver(ContentConsentFixture.CreateOffer(203, scriptCount: 1));
        Assert.Equal("script-execution", PasswordPrompt.Owner);
        int acknowledgements = AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>().Count();
        staleApproval();
        Assert.Equal(0, ScriptingBootstrap.PrepareCalls);
        Assert.Equal(0, ScriptingBootstrap.PreparedScriptCount);
        Assert.Equal(acknowledgements, AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>().Count());
        Assert.False(ContentCatalogClient.IsActivated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableOrThrowingPreparationCannotSendActivated(bool throws)
    {
        ContentConsentFixture.Deliver(ContentConsentFixture.CreateOffer(104, scriptCount: 1));
        PasswordPrompt.Approve();
        ScriptingBootstrap.ScriptsReady = throws;
        ScriptingBootstrap.ThrowOnPrepare = throws;
        PasswordPrompt.Approve();
        Assert.Equal(1, ScriptingBootstrap.PrepareCalls);
        Assert.Equal(0, ScriptingBootstrap.PreparedScriptCount);
        Assert.False(ContentCatalogClient.IsActivated);
        Assert.Equal(ContentCatalogActivationStatus.Failed,
            Assert.Single(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>()).Status);
        ContentConsentFixture.DrainGrace();
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void InjectedSourceHashValidationFailureNeverPromptsOrCallsPreparation()
    {
        ScriptPackManager.PreparationError = "verified source hash mismatch";
        ContentConsentFixture.Deliver(ContentConsentFixture.CreateOffer(105, scriptCount: 1));
        PasswordPrompt.Approve();
        Assert.Equal(new[] { "content-catalog" }, PasswordPrompt.Owners);
        Assert.Equal(0, ScriptingBootstrap.PrepareCalls);
        Assert.False(ContentCatalogClient.IsActivated);
        Assert.Equal(ContentCatalogActivationStatus.Failed,
            Assert.Single(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>()).Status);
    }

    [Fact]
    public void DataOnlyOfferPreparesWithoutExecutionApprovalPrompt()
    {
        ContentConsentFixture.Deliver(ContentConsentFixture.CreateOffer(106, scriptCount: 0));
        PasswordPrompt.Approve();
        Assert.Equal(new[] { "content-catalog" }, PasswordPrompt.Owners);
        Assert.Equal(new[] { false }, ScriptingBootstrap.PrepareApprovals);
        Assert.Equal(0, ScriptingBootstrap.PreparedScriptCount);
        Assert.True(ContentCatalogClient.IsActivated);
    }

    [Fact]
    public void UnqualifiedScriptOfferClosesBeforeConsentDownloadOrVmPreparation()
    {
        Anomaly.Client.Api.Scripting.ScriptingPolicy.RemoteExecutionEnabled = false;
        ContentConsentFixture.Deliver(ContentConsentFixture.CreateOffer(108, scriptCount: 1));
        Assert.Empty(PasswordPrompt.Owners);
        Assert.Empty(AnomalyMessaging.Sent.OfType<FileRequestMessage>());
        Assert.Equal(0, ScriptPackManager.Preparations);
        Assert.Equal(0, ScriptingBootstrap.PrepareCalls);
        Assert.False(ContentCatalogClient.IsActivated);
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
        Assert.Equal(ContentCatalogDecision.Declined,
            Assert.Single(AnomalyMessaging.Sent.OfType<ContentCatalogDecisionMessage>()).Decision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScriptOnlyOfferUsesTheNormalConsentAndAdmissionFlow(bool emptySource)
    {
        var offer = ContentConsentFixture.CreateOffer(107, scriptCount: 1);
        offer.Catalog.Packs.Clear();
        offer.Manifest.Files.RemoveAll(file => file.Kind == FileContentKind.Bundle || file.Kind == FileContentKind.PackIndex);
        if (emptySource)
        {
            var source = offer.Catalog.ScriptPacks[0].Sources[0];
            source.Size = 0;
            source.Hash = Anomaly.Shared.Scripting.ScriptSourceValidator.Sha256(Array.Empty<byte>());
            offer.Manifest.Files.RemoveAll(file => file.Kind == FileContentKind.LuaSource);
        }
        offer.Manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(offer.Manifest.Files);
        offer.Catalog.ManifestSha256 = (byte[])offer.Manifest.ManifestSha256.Clone();
        offer.Catalog.CatalogSha256 = CatalogBuilder.ComputeCatalogHash(offer.Catalog);
        ContentConsentFixture.Deliver(offer);
        Assert.Equal("content-catalog", PasswordPrompt.Owner);
        PasswordPrompt.Approve();
        Assert.Equal("script-execution", PasswordPrompt.Owner);
        Assert.False(ContentCatalogClient.IsActivated);
        PasswordPrompt.Approve();
        Assert.True(ContentCatalogClient.IsActivated);
        Assert.Equal(1, ScriptingBootstrap.PreparedScriptCount);
        Assert.Equal(1, ScriptPackManager.Preparations);
        Assert.Equal(0, NetworkClient.ReadyCalls);
        Assert.Equal(0, NetworkClient.AddPlayerCalls);
    }

    public void Dispose() => ContentConsentFixture.Reset();
}

internal static class ContentConsentFixture
{
    internal sealed class Offer
    {
        internal Offer(ContentCatalog catalog, FileManifestMessage manifest) { Catalog = catalog; Manifest = manifest; }
        internal ContentCatalog Catalog { get; }
        internal FileManifestMessage Manifest { get; }
    }
    internal static void Begin()
    {
        Anomaly.Client.Auth.AuthHandler.UsingAnomalyAuth = true;
        NetworkClient.connection ??= new NetworkConnection();
        ConnectionContentBootstrap.Initialize();
        ConnectionContentBootstrap.BeginConnection();
        ConnectionContentBootstrap.BeginFacilityGeneration(1);
    }

    internal static Offer CreateOffer(ulong sessionId, int scriptCount)
    {
        byte[] bundle = Enumerable.Repeat((byte)0x61, 32).ToArray(), index = Enumerable.Repeat((byte)0x62, 32).ToArray();
        var manifest = new FileManifestMessage { SessionId = sessionId, Files =
        {
            new FileManifestMessage.FileEntry("fixture.bundle", bundle, 64, FileContentKind.Bundle),
            new FileManifestMessage.FileEntry("fixture.index.json", index, 512, FileContentKind.PackIndex),
        } };
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
        var catalog = CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 1, 0, manifest.ManifestSha256, new[]
        {
            new CatalogPack { PackId = "fixture:assets", BundleHash = bundle, BundleSize = 64, IndexHash = index, IndexSize = 512 },
        }, scriptPacks: scriptCount == 0 ? Array.Empty<CatalogScriptPack>() : new[] { new CatalogScriptPack
        {
            PackId = "fixture:scripts", IndexHash = Enumerable.Repeat((byte)0x63, 32).ToArray(), IndexSize = 256,
            Sources = Enumerable.Range(0, scriptCount).Select(number => new CatalogScriptSource
            {
                Address = "mods/fixture/scripts/" + number + ".lua", Hash = Enumerable.Repeat((byte)(0x64 + number), 32).ToArray(), Size = 8,
            }).ToList(),
        } });
        foreach (var pack in catalog.ScriptPacks)
        {
            manifest.Files.Add(new FileManifestMessage.FileEntry(Hex(pack.IndexHash) + ".scripts.json", pack.IndexHash, pack.IndexSize, FileContentKind.ScriptIndex));
            foreach (var source in pack.Sources)
                manifest.Files.Add(new FileManifestMessage.FileEntry(Hex(source.Hash) + ".lua", source.Hash, source.Size, FileContentKind.LuaSource));
        }
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
        catalog.ManifestSha256 = (byte[])manifest.ManifestSha256.Clone();
        catalog.CatalogSha256 = CatalogBuilder.ComputeCatalogHash(catalog);
        ContentCatalogMessage.ValidateCatalog(catalog, validateDeclaredHash: true);
        using (var bytes = new System.IO.MemoryStream())
        using (var writer = new System.IO.BinaryWriter(bytes)) manifest.Serialize(writer);
        return new Offer(catalog, manifest);
    }

    internal static void Deliver(Offer offer)
    {
        typeof(ContentCatalogClient).GetMethod("HandleMessage", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { new ContentCatalogMessage { Catalog = offer.Catalog } });
        typeof(FileTransferClient).GetMethod("HandleManifest", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { offer.Manifest });
    }

    internal static void DrainGrace()
    {
        foreach (IEnumerator routine in MelonCoroutines.Started.ToArray())
        {
            Assert.True(routine.MoveNext());
            UnityEngine.Time.realtimeSinceStartupAsDouble = 0.25;
            Assert.False(routine.MoveNext());
        }
    }

    internal static void Reset()
    {
        Anomaly.Client.Auth.AuthHandler.UsingAnomalyAuth = false;
        Anomaly.Client.Api.Scripting.ScriptingPolicy.RemoteExecutionEnabled = true;
        ScriptingBootstrap.ThrowOnRevoke = false;
        ClientContentRegistrar.ThrowOnReset = false;
        AssetBundleManager.ThrowOnRevokeRemote = false;
        FileTransferOverlay.ThrowOnHide = false;
        MelonLogger.ThrowOnError = MelonLogger.ThrowOnWarning = false;
        ConnectionContentBootstrap.Revoke();
        ContentCatalogClient.Reset(); FileTransferClient.Reset();
        ContentConsentStore.Approved.Clear(); FileCache.HasFileResult = _ => false;
        ClientContentRegistrar.Activations = 0;
        AssetBundleManager.ActivationError = null;
        ScriptPackManager.PreparationError = null;
        ScriptPackManager.Preparations = 0;
        AssetBundleManager.NativeLeaseOwned = false;
        AssetBundleManager.ClearRemoteCalls = 0;
        AssetBundleManager.LastActivationCallback = null;
        PasswordPrompt.ResetForTests(); FileTransferOverlay.Hide();
        AnomalyMessaging.ResetForTests();
        FacilityRecipeClient.IsReady = false;
        NetworkClient.ReadyCalls = NetworkClient.AddPlayerCalls = ClientRuntime.ReadyPublications = 0;
        ScriptingBootstrap.ResetForTests();
    }

    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
}
